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
    public sealed class ResourceService : IResourceService
    {
        private readonly IResourceProvider m_provider;

        /// <summary>AssetEntry 表：key → 已加载资源记录。</summary>
        private readonly Dictionary<AssetKey, AssetEntry> m_entries =
            new Dictionary<AssetKey, AssetEntry>();

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

            return Attach(key, typed, typeof(T));
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

            IAwaitable<UnityEngine.Object> pending;
            try
            {
                pending = m_provider.LoadAsync(key, typeof(T), ct);
            }
            catch (Exception e)
            {
                source.SetException(e);
                return source;
            }

            if (pending == null)
            {
                source.SetException(new ResourceException(key.ToString(), null, ResourceStage.AssetLoad, "Provider 未返回加载任务"));
                return source;
            }

            pending.Completed += delegate
            {
                if (ct.IsCancellationRequested)
                {
                    source.SetException(new OperationCanceledException());
                    return;
                }

                try
                {
                    T typed = pending.GetResult() as T;
                    if (typed == null)
                    {
                        throw new ResourceException(key.ToString(), null, ResourceStage.AssetLoad,
                            string.Format("资源类型不匹配，期望 {0}", typeof(T).Name));
                    }

                    source.SetResult(Attach(key, typed, typeof(T)));
                }
                catch (Exception e)
                {
                    Log.Error("ResourceService", string.Format("加载失败：{0}", key), e);
                    source.SetException(e);
                }
            };

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

        private IAssetHandle<T> Attach<T>(AssetKey key, T asset, Type type) where T : UnityEngine.Object
        {
            int refCount;
            lock (m_entries)
            {
                AssetEntry entry;
                if (!m_entries.TryGetValue(key, out entry))
                {
                    entry = new AssetEntry
                    {
                        Key = key,
                        Asset = asset,
                        AssetType = type
                    };
                    m_entries[key] = entry;
                }

                entry.Asset = asset;
                refCount = entry.AddRef();
            }

            return new AssetHandle<T>(key, asset, refCount, OnHandleDisposed);
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

            // 计数归零：交给 Provider 决定立即释放还是进入延迟卸载
            m_provider.Release(key);
        }
    }
}
