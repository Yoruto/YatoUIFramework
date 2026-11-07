using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>
    /// 正式包资源后端：按 manifest 索引，依赖链递归加载，引用计数归零后延迟卸载。
    /// 取代 Bang- 的 ABManager / AssetBundleManager（RES-01~09）。
    /// </summary>
    public sealed class BundleProvider : IResourceProvider, IDisposable
    {
        private readonly ResourceManifest m_manifest;
        private readonly BundleLoader m_loader;
        private readonly BundleRefCounter m_refCounter;
        private readonly HashSet<AwaitableCompletionSource<UnityEngine.Object>> m_pending = new HashSet<AwaitableCompletionSource<UnityEngine.Object>>();
        private bool m_disposed;

        public BundleProvider(ResourceManifest manifest, BundleLoader loader, BundleRefCounter refCounter)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException("manifest");
            }

            if (loader == null)
            {
                throw new ArgumentNullException("loader");
            }

            if (refCounter == null)
            {
                throw new ArgumentNullException("refCounter");
            }

            m_manifest = manifest;
            m_loader = loader;
            m_refCounter = refCounter;
            m_refCounter.Hook();
        }

        public ResourceManifest Manifest
        {
            get { return m_manifest; }
        }

        public ResourceStats Stats
        {
            get { return m_refCounter.CreateStats(); }
        }

        public IAwaitable<UnityEngine.Object> LoadAsync(AssetKey key, Type type, CancellationToken ct)
        {
            var source = new AwaitableCompletionSource<UnityEngine.Object>();
            if (m_disposed)
            {
                source.SetException(new ObjectDisposedException(nameof(BundleProvider)));
                return source;
            }
            m_pending.Add(source);
            source.Completed += () => m_pending.Remove(source);

            ResourceManifest.BundleInfo bundle;
            if (!m_manifest.TryGetBundleOfAsset(key.ToString(), out bundle))
            {
                source.SetException(new ResourceException(
                    key.ToString(),
                    null,
                    ResourceStage.ManifestLoad,
                    "manifest 中不存在该资源，请检查 key 或重新打包"));
                return source;
            }

            List<ResourceManifest.BundleInfo> order;
            try
            {
                // 依赖在前、自身在最后；循环依赖在这里抛出
                order = m_manifest.ResolveLoadOrder(bundle.Name);
            }
            catch (Exception e)
            {
                source.SetException(e);
                return source;
            }

            LoadChain(order, 0, ct, key, type, source);
            return source;
        }

        /// <summary>资源释放：其所属 bundle 与整条依赖链各减一次引用。</summary>
        public void Release(AssetKey key)
        {
            if (m_disposed) return;
            ResourceManifest.BundleInfo bundle;
            if (!m_manifest.TryGetBundleOfAsset(key.ToString(), out bundle))
            {
                return;
            }

            List<ResourceManifest.BundleInfo> order;
            try
            {
                order = m_manifest.ResolveLoadOrder(bundle.Name);
            }
            catch (Exception e)
            {
                Log.Error("BundleProvider", string.Format("释放时解析依赖失败：{0}", key), e);
                return;
            }

            for (int i = order.Count - 1; i >= 0; i--)
            {
                m_refCounter.Release(order[i].Name);
            }
        }

        public void ReleaseUnused()
        {
            if (m_disposed) return;
            m_refCounter.FlushExpired();
        }

        /// <summary>应用退出 / 场景切换时全量卸载。</summary>
        public void Shutdown()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            var pending = new List<AwaitableCompletionSource<UnityEngine.Object>>(m_pending);
            m_pending.Clear();
            foreach (var source in pending) source.SetException(new OperationCanceledException("Bundle provider shut down"));
            m_loader.Dispose();
            m_refCounter.FlushAll();
            m_refCounter.Unhook();
        }

        private void LoadChain(
            List<ResourceManifest.BundleInfo> order,
            int index,
            CancellationToken ct,
            AssetKey key,
            Type type,
            AwaitableCompletionSource<UnityEngine.Object> source)
        {
            if (m_disposed || ct.IsCancellationRequested)
            {
                // 已经加过引用的前半段必须回滚，否则失败请求会永久持有 bundle
                ReleaseChain(order, index);
                source.SetException(new OperationCanceledException());
                return;
            }

            if (index >= order.Count)
            {
                // 依赖链全部就绪后才加载资产本身
                LoadAsset(order[order.Count - 1].Name, key, type, order, source, ct);
                return;
            }

            ResourceManifest.BundleInfo info = order[index];

            IAwaitable loading;
            try
            {
                loading = m_loader.LoadAsync(info.Name, ct);
            }
            catch (Exception e)
            {
                // 同步抛出时任务永远不会完成，必须在这里结束并回滚
                ReleaseChain(order, index);
                source.SetException(new ResourceException(
                    key.ToString(), info.Name, ResourceStage.BundleLoad, "bundle 加载失败", e));
                return;
            }

            loading.Completed += delegate
            {
                try
                {
                    loading.GetResult();
                    m_refCounter.AddRef(info.Name);
                    LoadChain(order, index + 1, ct, key, type, source);
                }
                catch (Exception e)
                {
                    // 本层之前的 [0, index) 已 AddRef，失败必须逐层归还
                    ReleaseChain(order, index);
                    if (e is OperationCanceledException) { source.SetException(e); return; }
                    source.SetException(new ResourceException(
                        key.ToString(),
                        info.Name,
                        ResourceStage.BundleLoad,
                        "bundle 加载失败",
                        e));
                }
            };
        }

        private void LoadAsset(
            string bundleName,
            AssetKey key,
            Type type,
            List<ResourceManifest.BundleInfo> order,
            AwaitableCompletionSource<UnityEngine.Object> source,
            CancellationToken ct)
        {
            AssetBundle bundle = m_loader.GetBundle(bundleName);
            if (bundle == null)
            {
                ReleaseChain(order, order.Count);
                source.SetException(new ResourceException(
                    key.ToString(), bundleName, ResourceStage.AssetLoad, "bundle 未就绪"));
                return;
            }

            string assetName;
            try { assetName = ResolveAssetName(bundle, key); }
            catch (Exception error)
            {
                ReleaseChain(order, order.Count);
                source.SetException(new ResourceException(key.ToString(), bundleName,
                    ResourceStage.AssetLoad, "Ambiguous asset mapping in bundle", error));
                return;
            }
            if (string.IsNullOrEmpty(assetName))
            {
                ReleaseChain(order, order.Count);
                source.SetException(new ResourceException(
                    key.ToString(), bundleName, ResourceStage.AssetLoad,
                    "bundle 内找不到该资产，请检查 manifest 的 assets 映射"));
                return;
            }

            AssetBundleRequest request;
            try
            {
                request = bundle.LoadAssetAsync(assetName, type);
            }
            catch (Exception e)
            {
                ReleaseChain(order, order.Count);
                source.SetException(new ResourceException(
                    key.ToString(), bundleName, ResourceStage.AssetLoad, "LoadAssetAsync 失败", e));
                return;
            }

            if (request == null)
            {
                ReleaseChain(order, order.Count);
                source.SetException(new ResourceException(
                    key.ToString(), bundleName, ResourceStage.AssetLoad, "LoadAssetAsync 返回空"));
                return;
            }

            request.completed += delegate
            {
                try
                {
                    if (m_disposed || ct.IsCancellationRequested)
                        throw new OperationCanceledException(ct);
                    UnityEngine.Object asset = request.asset;
                    if (asset == null)
                    {
                        throw new ResourceException(
                            key.ToString(), bundleName, ResourceStage.AssetLoad, "资产为空：" + assetName);
                    }

                    source.SetResult(asset);
                }
                catch (Exception e)
                {
                    ReleaseChain(order, order.Count);
                    source.SetException(e);
                }
            };
        }

        /// <summary>
        /// 回滚依赖链前 <paramref name="exclusiveEnd"/> 个 bundle 的引用（按加载顺序倒序释放）。
        /// 失败路径必须调用，保证 Provider 与 <see cref="ResourceService"/> 的计数对称。
        /// </summary>
        private void ReleaseChain(List<ResourceManifest.BundleInfo> order, int exclusiveEnd)
        {
            if (order == null || exclusiveEnd <= 0)
            {
                return;
            }

            int end = exclusiveEnd > order.Count ? order.Count : exclusiveEnd;
            for (int i = end - 1; i >= 0; i--)
            {
                m_refCounter.Release(order[i].Name);
            }
        }

        /// <summary>
        /// Match the complete logical key first. A basename-only lookup can silently return
        /// the wrong asset when two directories contain files with the same name.
        /// A unique-basename fallback supports bundles from older custom build pipelines.
        /// </summary>
        private static string ResolveAssetName(AssetBundle bundle, AssetKey key)
        {
            return ResolveAssetName(bundle.GetAllAssetNames(), key);
        }

        internal static string ResolveAssetName(IReadOnlyList<string> names, AssetKey key)
        {
            string value = key.Value;
            int slash = value.LastIndexOf('/');
            string leaf = slash >= 0 ? value.Substring(slash + 1) : value;

            string fullMatch = null;
            string leafMatch = null;
            int leafCount = 0;
            string suffix = "/" + value;
            for (int i = 0; i < names.Count; i++)
            {
                string path = names[i].Replace('\\', '/');
                string withoutExtension = path.Substring(0, path.Length - Path.GetExtension(path).Length);
                if (string.Equals(withoutExtension, value, StringComparison.OrdinalIgnoreCase) ||
                    withoutExtension.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (fullMatch != null)
                        throw new InvalidOperationException("Multiple assets match the full key: " + value);
                    fullMatch = names[i];
                }
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), leaf, StringComparison.OrdinalIgnoreCase))
                    continue;
                leafMatch = names[i];
                leafCount++;
            }
            if (fullMatch != null) return fullMatch;
            if (leafCount > 1)
                throw new InvalidOperationException("Multiple assets share a basename: " + leaf);
            return leafMatch;
        }
    }
}
