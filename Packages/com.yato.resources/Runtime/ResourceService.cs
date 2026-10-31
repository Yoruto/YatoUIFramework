using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>
    /// 资源门面：key 解析、Provider 选择、句柄与引用计数管理。
    /// 不再包含任何 Resources.Load / AssetBundle 细节（RES-09/RES-15 的修复）。
    /// </summary>
    /// <remarks>
    /// Provider 引用与句柄引用必须一一对应：
    /// 同一个 key 的并发加载单飞（只向 Provider 申请一次引用），
    /// 句柄计数归零时只归还这一次引用；若没有任何消费者接管，则立刻归还，避免泄漏。
    /// </remarks>
    public sealed class ResourceService : IResourceService
    {
        private readonly IResourceProvider m_provider;

        /// <summary>AssetEntry 表：key → 已加载资源记录。</summary>
        private readonly Dictionary<AssetKey, AssetEntry> m_entries =
            new Dictionary<AssetKey, AssetEntry>();

        /// <summary>进行中的 Provider 加载：同一 key 只申请一次 Provider 引用（单飞）。</summary>
        private readonly Dictionary<AssetKey, PendingLoad> m_pendings =
            new Dictionary<AssetKey, PendingLoad>();

        public ResourceService(IResourceProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException("provider");
            }

            m_provider = provider;
        }

        public IResourceProvider Provider
        {
            get { return m_provider; }
        }

        public ResourceStats Stats
        {
            get { return m_provider.Stats ?? ResourceStats.Empty; }
        }

        /// <summary>当前被句柄持有的资产数（诊断用）。</summary>
        public int ActiveAssetCount
        {
            get
            {
                lock (m_entries)
                {
                    return m_entries.Count;
                }
            }
        }

        public IAssetHandle<T> Load<T>(AssetKey key) where T : UnityEngine.Object
        {
            if (!key.IsValid)
            {
                throw new ResourceException(null, null, ResourceStage.KeyNormalize, "资源 key 非法");
            }

            AssetEntry loaded;
            lock (m_entries)
            {
                if (m_entries.TryGetValue(key, out loaded) && loaded.Asset != null)
                {
                    T cached = loaded.Asset as T;
                    if (cached == null)
                    {
                        throw new ResourceException(key.ToString(), null, ResourceStage.AssetLoad,
                            string.Format("资源类型不匹配，期望 {0}", typeof(T).Name));
                    }

                    return CreateHandle(key, cached, loaded);
                }
            }

            IAwaitable<UnityEngine.Object> pending = m_provider.LoadAsync(key, typeof(T), CancellationToken.None);
            if (pending == null)
            {
                throw new ResourceException(key.ToString(), null, ResourceStage.AssetLoad, "Provider 未返回加载任务");
            }

            if (!pending.IsDone)
            {
                throw new ResourceException(key.ToString(), null, ResourceStage.AssetLoad,
                    "当前 Provider 不支持同步加载，请改用 LoadAsync");
            }

            T typed = pending.GetResult() as T;
            if (typed == null)
            {
                throw new ResourceException(key.ToString(), null, ResourceStage.AssetLoad,
                    string.Format("资源类型不匹配，期望 {0}", typeof(T).Name));
            }

            return AttachDirect(key, typed, typeof(T));
        }

        public IAwaitable<IAssetHandle<T>> LoadAsync<T>(AssetKey key, CancellationToken ct = default(CancellationToken))
            where T : UnityEngine.Object
        {
            var source = new AwaitableCompletionSource<IAssetHandle<T>>();

            if (!key.IsValid)
            {
                source.SetException(new ResourceException(null, null, ResourceStage.KeyNormalize, "资源 key 非法"));
                return source;
            }

            if (ct.IsCancellationRequested)
            {
                source.SetException(new OperationCanceledException());
                return source;
            }

            // 已加载：直接复用，不再向 Provider 申请引用（避免 Provider 计数只增不减）
            AssetEntry loaded;
            lock (m_entries)
            {
                if (m_entries.TryGetValue(key, out loaded) && loaded.Asset != null)
                {
                    T cached = loaded.Asset as T;
                    if (cached == null)
                    {
                        source.SetException(new ResourceException(
                            key.ToString(), null, ResourceStage.AssetLoad,
                            string.Format("资源类型不匹配，期望 {0}", typeof(T).Name)));
                        return source;
                    }

                    source.SetResult(CreateHandle(key, cached, loaded));
                    return source;
                }
            }

            PendingLoad pending;
            try
            {
                pending = AcquirePending(key, typeof(T), ct);
            }
            catch (Exception e)
            {
                source.SetException(e);
                return source;
            }

            Action<PendingLoad> waiter = delegate(PendingLoad p)
            {
                // 加载完成前取消：不接管 Provider 引用（CompletePending 会在无人接管时归还）
                if (ct.IsCancellationRequested)
                {
                    source.SetException(new OperationCanceledException());
                    return;
                }

                if (p.Error != null)
                {
                    source.SetException(p.Error);
                    return;
                }

                try
                {
                    T typed = p.Asset as T;
                    if (typed == null)
                    {
                        throw new ResourceException(
                            key.ToString(), null, ResourceStage.AssetLoad,
                            string.Format("资源类型不匹配，期望 {0}", typeof(T).Name));
                    }

                    source.SetResult(ConsumePending(p, typed, typeof(T)));
                }
                catch (Exception e)
                {
                    Log.Error("ResourceService", string.Format("加载失败：{0}", key), e);
                    source.SetException(e);
                }
            };

            // 先登记等待者再订阅 Provider 完成事件：
            // Provider 同步完成时，CompletePending 必须能看到至少一个消费者，否则会误判为无人接管而提前归还引用。
            pending.AddWaiter(waiter);
            pending.Subscribe(delegate(PendingLoad p) { CompletePending(p); });
            return source;
        }

        public IAwaitable<IAssetHandle<GameObject>> InstantiateAsync(
            AssetKey key,
            Transform parent,
            CancellationToken ct = default(CancellationToken))
        {
            var source = new AwaitableCompletionSource<IAssetHandle<GameObject>>();
            IAwaitable<IAssetHandle<GameObject>> loading = LoadAsync<GameObject>(key, ct);

            loading.Completed += delegate
            {
                IAssetHandle<GameObject> prefabHandle = null;
                try
                {
                    prefabHandle = loading.GetResult();
                    GameObject prefab = prefabHandle.Asset;
                    if (prefab == null)
                    {
                        throw new ResourceException(key.ToString(), null, ResourceStage.Instantiate, "预制体为空");
                    }

                    GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
                    IAssetHandle<GameObject> capturedPrefab = prefabHandle;

                    // 实例句柄：Dispose 时销毁实例并释放预制体句柄
                    var handle = new AssetHandle<GameObject>(key, instance, 1, delegate
                    {
                        if (instance != null)
                        {
                            UnityEngine.Object.Destroy(instance);
                        }

                        capturedPrefab.Dispose();
                    });

                    source.SetResult(handle);
                }
                catch (Exception e)
                {
                    if (prefabHandle != null)
                    {
                        prefabHandle.Dispose();
                    }

                    Log.Error("ResourceService", string.Format("实例化失败：{0}", key), e);
                    source.SetException(e);
                }
            };

            return source;
        }

        public IAwaitable PreloadAsync(
            IReadOnlyList<AssetKey> keys,
            IProgress<float> progress = null,
            CancellationToken ct = default(CancellationToken))
        {
            var source = new AwaitableCompletionSource();

            if (keys == null || keys.Count == 0)
            {
                if (progress != null)
                {
                    progress.Report(1f);
                }

                source.SetResult();
                return source;
            }

            int index = 0;
            Action step = null;

            // 逐个加载并立即释放：bundle 进入延迟卸载窗口，二次打开命中缓存
            step = delegate
            {
                if (ct.IsCancellationRequested)
                {
                    source.SetException(new OperationCanceledException());
                    return;
                }

                if (index >= keys.Count)
                {
                    if (progress != null)
                    {
                        progress.Report(1f);
                    }

                    source.SetResult();
                    return;
                }

                AssetKey key = keys[index];
                IAwaitable<IAssetHandle<UnityEngine.Object>> loading = LoadAsync<UnityEngine.Object>(key, ct);

                loading.Completed += delegate
                {
                    try
                    {
                        IAssetHandle<UnityEngine.Object> handle = loading.GetResult();
                        handle.Dispose();
                    }
                    catch (Exception e)
                    {
                        Log.Warn(string.Format("[ResourceService] 预加载失败：{0}（{1}）", key, e.Message));
                    }

                    index++;
                    if (progress != null)
                    {
                        progress.Report((float)index / keys.Count);
                    }

                    step();
                };
            };

            step();
            return source;
        }

        public void Release(IAssetHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            // 真正的引用计数递减在句柄 Dispose 回调中完成（幂等）
            handle.Dispose();
        }

        public void ReleaseUnused()
        {
            m_provider.ReleaseUnused();
        }

        /// <summary>同步加载路径：Provider 引用直接归入新建的 AssetEntry。</summary>
        private IAssetHandle<T> AttachDirect<T>(AssetKey key, T asset, Type type) where T : UnityEngine.Object
        {
            AssetEntry entry;
            lock (m_entries)
            {
                if (!m_entries.TryGetValue(key, out entry))
                {
                    entry = new AssetEntry
                    {
                        Key = key,
                        AssetType = type
                    };
                    m_entries[key] = entry;
                }

                entry.Asset = asset;
            }

            return CreateHandle(key, asset, entry);
        }

        /// <summary>异步加载路径：把本次 Provider 引用移交给 AssetEntry（只移交一次）。</summary>
        private IAssetHandle<T> ConsumePending<T>(PendingLoad pending, T asset, Type type)
            where T : UnityEngine.Object
        {
            AssetEntry entry;
            bool created = false;

            lock (m_entries)
            {
                if (!m_entries.TryGetValue(pending.Key, out entry))
                {
                    entry = new AssetEntry
                    {
                        Key = pending.Key,
                        AssetType = type
                    };
                    m_entries[pending.Key] = entry;
                    created = true;
                }

                entry.Asset = asset;
            }

            if (created)
            {
                pending.Consumed = true;
            }
            else if (!pending.Consumed)
            {
                // 兜底：记录不是本次加载创建的，说明这次 Provider 引用是多余的，立即归还
                m_provider.Release(pending.Key);
            }

            return CreateHandle(pending.Key, asset, entry);
        }

        private IAssetHandle<T> CreateHandle<T>(AssetKey key, T asset, AssetEntry entry)
            where T : UnityEngine.Object
        {
            AssetEntry captured = entry;
            lock (m_entries)
            {
                captured.AddRef();
            }

            // RefCount 使用实时读取，避免诊断值停留在创建时的快照
            return new AssetHandle<T>(key, asset, delegate { return captured.RefCount; }, OnHandleDisposed);
        }

        private PendingLoad AcquirePending(AssetKey key, Type type, CancellationToken ct)
        {
            PendingLoad pending;
            lock (m_pendings)
            {
                if (m_pendings.TryGetValue(key, out pending))
                {
                    return pending;
                }

                pending = new PendingLoad { Key = key };
                m_pendings[key] = pending;
            }

            IAwaitable<UnityEngine.Object> task;
            try
            {
                task = m_provider.LoadAsync(key, type, ct);
            }
            catch (Exception)
            {
                RemovePending(pending);
                throw;
            }

            if (task == null)
            {
                RemovePending(pending);
                throw new ResourceException(key.ToString(), null, ResourceStage.AssetLoad, "Provider 未返回加载任务");
            }

            pending.Task = task;
            return pending;
        }

        private void RemovePending(PendingLoad pending)
        {
            lock (m_pendings)
            {
                PendingLoad registered;
                if (m_pendings.TryGetValue(pending.Key, out registered) && ReferenceEquals(registered, pending))
                {
                    m_pendings.Remove(pending.Key);
                }
            }
        }

        private void CompletePending(PendingLoad pending)
        {
            RemovePending(pending);

            if (pending.Done)
            {
                return;
            }

            try
            {
                pending.Asset = pending.Task.GetResult();
            }
            catch (Exception e)
            {
                pending.Error = e;
            }

            if (pending.Error == null && pending.Asset == null)
            {
                pending.Error = new ResourceException(
                    pending.Key.ToString(), null, ResourceStage.AssetLoad, "Provider 返回了空资源");
            }

            pending.Done = true;

            // 加载失败必须留痕：单飞后的失败只记录一次，而不是每个等待者各记一次
            if (pending.Error != null)
            {
                Log.Error("ResourceService", string.Format("加载失败：{0}", pending.Key), pending.Error);
            }

            List<Action<PendingLoad>> waiters = pending.TakeWaiters();
            for (int i = 0; i < waiters.Count; i++)
            {
                try
                {
                    waiters[i](pending);
                }
                catch (Exception e)
                {
                    Log.Error("ResourceService", string.Format("加载回调异常：{0}", pending.Key), e);
                }
            }

            // 没有任何消费者接管这次 Provider 引用（全部取消 / 类型不符）时立即归还
            if (!pending.Consumed && pending.Error == null)
            {
                m_provider.Release(pending.Key);
            }
        }

        private void OnHandleDisposed(IAssetHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            AssetKey key = handle.Key;
            lock (m_entries)
            {
                AssetEntry entry;
                if (!m_entries.TryGetValue(key, out entry))
                {
                    return;
                }

                int remaining = entry.Release();
                if (remaining > 0)
                {
                    return;
                }

                m_entries.Remove(key);
                entry.Asset = null;
            }

            // 计数归零：交给 Provider 决定立即释放还是进入延迟卸载。
            // 单飞保证这里只归还一次 Provider 引用。
            m_provider.Release(key);
        }

        /// <summary>一次进行中的 Provider 加载，可被多个等待者共享。</summary>
        private sealed class PendingLoad
        {
            public AssetKey Key;
            public IAwaitable<UnityEngine.Object> Task;
            public UnityEngine.Object Asset;
            public Exception Error;
            public bool Done;

            /// <summary>是否已有等待者接管本次 Provider 引用。</summary>
            public bool Consumed;

            private List<Action<PendingLoad>> m_waiters = new List<Action<PendingLoad>>();
            private bool m_subscribed;

            public void AddWaiter(Action<PendingLoad> waiter)
            {
                if (waiter == null)
                {
                    return;
                }

                if (Done)
                {
                    waiter(this);
                    return;
                }

                m_waiters.Add(waiter);
            }

            /// <summary>
            /// 订阅 Provider 完成事件。必须在等待者登记之后调用：
            /// Provider 可能已经同步完成，回调会立即执行。
            /// </summary>
            public void Subscribe(Action<PendingLoad> onCompleted)
            {
                if (m_subscribed || Task == null)
                {
                    return;
                }

                m_subscribed = true;
                Task.Completed += delegate { onCompleted(this); };
            }

            public List<Action<PendingLoad>> TakeWaiters()
            {
                List<Action<PendingLoad>> waiters = m_waiters;
                m_waiters = new List<Action<PendingLoad>>();
                return waiters;
            }
        }
    }
}
