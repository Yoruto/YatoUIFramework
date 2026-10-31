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
    /// <para>取消按调用者隔离：单个 LoadAsync 的取消只结束自己的等待，
    /// 只有全部消费者都放弃时才中止底层 Provider 加载。</para>
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

            // 同步路径由调用方独占一次 Provider 请求，必须自己持有取消权并负责回滚
            var cancellation = new CancellationTokenSource();
            IAwaitable<UnityEngine.Object> task;
            try
            {
                task = m_provider.LoadAsync(key, typeof(T), cancellation.Token);
            }
            catch (Exception)
            {
                cancellation.Dispose();
                throw;
            }

            if (task == null)
            {
                cancellation.Dispose();
                throw new ResourceException(key.ToString(), null, ResourceStage.AssetLoad, "Provider 未返回加载任务");
            }

            if (!task.IsDone)
            {
                // 无法同步返回句柄：取消这次加载，并接管「Provider 不响应取消但最终成功」的引用回收
                AbandonSynchronousLoad(key, task, cancellation);
                throw new ResourceException(key.ToString(), null, ResourceStage.AssetLoad,
                    "当前 Provider 不支持同步加载，请改用 LoadAsync");
            }

            cancellation.Dispose();

            T typed = task.GetResult() as T;
            if (typed == null)
            {
                // 句柄无法建立：这次 Provider 引用必须立刻归还，否则 bundle 永远不会卸载
                m_provider.Release(key);
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
                pending = AcquirePending(key, typeof(T));
            }
            catch (Exception e)
            {
                source.SetException(e);
                return source;
            }

            // 每个调用者只订阅自己的取消：单个消费者取消不会中止共享的 Provider 加载
            var consumer = new PendingConsumer(
                delegate(PendingLoad p)
                {
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
                },
                delegate
                {
                    // 加载完成前取消：不接管 Provider 引用（CompletePending 会在无人接管时归还）
                    source.SetException(new OperationCanceledException());
                });

            // 先登记消费者再订阅 Provider 完成事件：
            // Provider 同步完成时，CompletePending 必须能看到至少一个消费者，否则会误判为无人接管而提前归还引用。
            pending.AddConsumer(consumer, ct);
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

        /// <summary>同步加载路径：Provider 引用直接归入新建的 AssetEntry（已有记录时归还多余引用）。</summary>
        private IAssetHandle<T> AttachDirect<T>(AssetKey key, T asset, Type type) where T : UnityEngine.Object
        {
            AssetEntry entry;
            bool created = false;

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
                    created = true;
                }

                entry.Asset = asset;
            }

            if (!created)
            {
                // 记录已被并发路径建立：本次同步加载申请的 Provider 引用是多余的，立即归还
                m_provider.Release(key);
            }

            return CreateHandle(key, asset, entry);
        }

        /// <summary>
        /// 同步加载无法立即返回句柄时调用：取消本次 Provider 请求，
        /// 并在任务最终仍然成功时归还这次无人接管的引用（同步路径不走单飞表，必须自行收尾）。
        /// </summary>
        private void AbandonSynchronousLoad(
            AssetKey key,
            IAwaitable<UnityEngine.Object> task,
            CancellationTokenSource cancellation)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (AggregateException e)
            {
                Log.Warn(string.Format("[ResourceService] 取消同步加载失败：{0}（{1}）", key, e.Message));
            }

            task.Completed += delegate
            {
                try
                {
                    // 失败/取消由 Provider 自行回滚；只有「取消后仍然成功」才需要 Service 归还引用
                    if (task.GetResult() != null)
                    {
                        m_provider.Release(key);
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    cancellation.Dispose();
                }
            };
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

        private PendingLoad AcquirePending(AssetKey key, Type type)
        {
            PendingLoad pending;
            lock (m_pendings)
            {
                if (m_pendings.TryGetValue(key, out pending))
                {
                    return pending;
                }

                pending = new PendingLoad(key);
                m_pendings[key] = pending;
            }

            IAwaitable<UnityEngine.Object> task;
            try
            {
                // Provider 只看到 PendingLoad 自己的令牌：任何单个调用者的取消都不会直接中止共享加载
                task = m_provider.LoadAsync(key, type, pending.ProviderToken);
            }
            catch (Exception)
            {
                RemovePending(pending);
                pending.DisposeCancellation();
                throw;
            }

            if (task == null)
            {
                RemovePending(pending);
                pending.DisposeCancellation();
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

            // 加载失败必须留痕：单飞后的失败只记录一次，而不是每个等待者各记一次
            if (pending.Error != null && !(pending.Error is OperationCanceledException))
            {
                Log.Error("ResourceService", string.Format("加载失败：{0}", pending.Key), pending.Error);
            }

            List<PendingConsumer> consumers = pending.Complete();
            for (int i = 0; i < consumers.Count; i++)
            {
                try
                {
                    consumers[i].Complete(pending);
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

            pending.DisposeCancellation();
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

        /// <summary>
        /// 一次进行中的 Provider 加载，可被多个消费者共享。
        /// <para>Provider 级取消与调用者级取消分离：只有全部消费者都放弃时才中止底层加载。</para>
        /// </summary>
        private sealed class PendingLoad
        {
            public readonly AssetKey Key;

            public IAwaitable<UnityEngine.Object> Task;
            public UnityEngine.Object Asset;
            public Exception Error;
            public bool Done;

            /// <summary>是否已有消费者接管本次 Provider 引用。</summary>
            public bool Consumed;

            private readonly object m_gate = new object();
            private readonly CancellationTokenSource m_cancellation = new CancellationTokenSource();
            private List<PendingConsumer> m_consumers = new List<PendingConsumer>();
            private int m_activeConsumers;
            private bool m_cancelRequested;
            private bool m_subscribed;

            public PendingLoad(AssetKey key)
            {
                Key = key;
            }

            /// <summary>传给 Provider 的令牌，由 PendingLoad 独占。</summary>
            public CancellationToken ProviderToken
            {
                get { return m_cancellation.Token; }
            }

            public void AddConsumer(PendingConsumer consumer, CancellationToken ct)
            {
                bool done;
                lock (m_gate)
                {
                    done = Done;
                    if (!done)
                    {
                        m_consumers.Add(consumer);
                        m_activeConsumers++;
                    }
                }

                if (done)
                {
                    consumer.Complete(this);
                    return;
                }

                // 先计入消费者再订阅取消：恰好在此时取消时不会误判为「没有消费者」
                consumer.Listen(this, ct);
            }

            /// <summary>某个消费者取消：只有最后一个消费者放弃时才中止 Provider 加载。</summary>
            public void OnConsumerCancelled()
            {
                bool cancel;
                lock (m_gate)
                {
                    if (m_activeConsumers > 0)
                    {
                        m_activeConsumers--;
                    }

                    cancel = m_activeConsumers == 0 && !Done && !m_cancelRequested;
                    if (cancel)
                    {
                        m_cancelRequested = true;
                    }
                }

                if (cancel)
                {
                    try
                    {
                        m_cancellation.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (AggregateException e)
                    {
                        Log.Warn(string.Format(
                            "[ResourceService] 中止底层加载失败：{0}（{1}）", Key, e.Message));
                    }
                }
            }

            /// <summary>标记完成并取出全部消费者（取消的消费者在回调里自行短路）。</summary>
            public List<PendingConsumer> Complete()
            {
                lock (m_gate)
                {
                    Done = true;
                    List<PendingConsumer> consumers = m_consumers;
                    m_consumers = new List<PendingConsumer>();
                    return consumers;
                }
            }

            public void DisposeCancellation()
            {
                try
                {
                    m_cancellation.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            /// <summary>
            /// 订阅 Provider 完成事件。必须在消费者登记之后调用：
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
        }

        /// <summary>
        /// 一次 LoadAsync 调用的消费者：自己的取消只影响自己；
        /// 只有全部消费者都取消时才通过 <see cref="PendingLoad.OnConsumerCancelled"/> 中止底层加载。
        /// </summary>
        private sealed class PendingConsumer
        {
            private readonly Action<PendingLoad> m_onCompleted;
            private readonly Action m_onCancelled;

            private PendingLoad m_pending;
            private CancellationTokenRegistration m_registration;
            private int m_finished;

            public PendingConsumer(Action<PendingLoad> onCompleted, Action onCancelled)
            {
                m_onCompleted = onCompleted;
                m_onCancelled = onCancelled;
            }

            public void Listen(PendingLoad pending, CancellationToken ct)
            {
                // 登记与完成竞争时加载已经结束：不再订阅取消，避免注册残留
                if (Volatile.Read(ref m_finished) != 0)
                {
                    return;
                }

                m_pending = pending;

                if (!ct.CanBeCanceled)
                {
                    return;
                }

                m_registration = ct.Register(Cancel);

                // token 已取消时 Register 会同步回调：补一次注销，避免注册残留
                if (Volatile.Read(ref m_finished) != 0)
                {
                    m_registration.Dispose();
                }
            }

            /// <summary>加载结束（成功/失败）：每个消费者最多回调一次。</summary>
            public void Complete(PendingLoad pending)
            {
                if (Interlocked.Exchange(ref m_finished, 1) != 0)
                {
                    return;
                }

                m_pending = null;
                m_registration.Dispose();
                m_onCompleted(pending);
            }

            private void Cancel()
            {
                if (Interlocked.Exchange(ref m_finished, 1) != 0)
                {
                    return;
                }

                PendingLoad pending = Interlocked.Exchange(ref m_pending, null);
                if (pending != null)
                {
                    pending.OnConsumerCancelled();
                }

                m_onCancelled();
            }
        }
    }
}
