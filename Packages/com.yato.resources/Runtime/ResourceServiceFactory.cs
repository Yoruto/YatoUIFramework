using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>
    /// 运行时 Provider 选择（文档 3.5）：
    /// 编辑器默认 AssetDatabase；强制 Bundle 或 Player 下按 manifest 选择 BundleProvider，
    /// Bundle 模式默认失败即报错；显式 AllowResourcesFallback 才降级。
    /// </summary>
    public enum ResourceFailurePolicy
    {
        BundleRequired,
        AllowResourcesFallback
    }

    public static class ResourceServiceFactory
    {
        /// <summary>编辑器下强制走 AssetBundle（用于验证打包结果）。</summary>
        public static bool ForceBundleMode;

        /// <summary>
        /// Run startup-only persistent cache maintenance before the first bundle provider is
        /// created. This trims previously committed snapshots that no loader uses yet and clears
        /// abandoned staging directories. Editor play mode and players both use it.
        /// </summary>
        public static bool AutoCleanupPersistentCache = true;

        private static bool s_cachePrepared;

        /// <summary>最近一次创建的服务实例（供诊断窗口读取统计）。</summary>
        public static IResourceService Current { get; private set; }

        internal static void Forget(IResourceService service)
        {
            if (ReferenceEquals(Current, service)) Current = null;
        }

        /// <summary>
        /// 关闭 Domain Reload 时静态状态不会自动清空，必须显式重置，
        /// 否则诊断窗口会读到上一会话的 service（对应文档 D02）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            (Current as IDisposable)?.Dispose();
            Current = null;
            ForceBundleMode = false;
            AutoCleanupPersistentCache = true;
            s_cachePrepared = false;
        }

        /// <summary>Default providers are service-owned; injected providers remain caller-owned unless ownsProvider is true.</summary>
        public static IResourceService Create(IResourceProvider provider = null) => Create(provider, false);

        public static IResourceService Create(IResourceProvider provider, bool ownsProvider)
        {
            var service = new ResourceService(provider ?? CreateProvider(), provider == null || ownsProvider);
            Current = service;
            return service;
        }

        /// <summary>
        /// Cross-platform entry point for bundled resources. Android/WebGL StreamingAssets are
        /// URLs and must be read asynchronously; synchronous Create remains available when the
        /// manifest is already on the local filesystem or an explicit provider is supplied.
        /// </summary>
        public static IAwaitable<IResourceService> CreateAsync(
            ResourceFailurePolicy policy = ResourceFailurePolicy.BundleRequired, string manifestPath = null,
            CancellationToken ct = default)
        {
            var result = new AwaitableCompletionSource<IResourceService>();
            var loading = CreateProviderAsync(policy, manifestPath, ct);
            loading.Completed += () =>
            {
                IResourceProvider provider = null;
                try
                {
                    provider = loading.GetResult();
                    var service = Create(provider, true);
                    provider = null;
                    result.SetResult(service);
                }
                catch (Exception error)
                {
                    (provider as IDisposable)?.Dispose();
                    result.SetException(error);
                }
            };
            return result;
        }

        public static IAwaitable<IResourceProvider> CreateProviderAsync(
            ResourceFailurePolicy policy = ResourceFailurePolicy.BundleRequired, string manifestPath = null,
            CancellationToken ct = default)
        {
            var result = new AwaitableCompletionSource<IResourceProvider>();
            try
            {
                ct.ThrowIfCancellationRequested();
#if UNITY_EDITOR
                if (!ForceBundleMode)
                {
                    result.SetResult(new EditorAssetProvider());
                    return result;
                }
#endif
                PreparePersistentCache();
                string path = manifestPath ?? ResolveManifestPath();
                if (IsRemoteManifestPath(path))
                    RuntimeHost.RunCoroutine(LoadManifestFromUrl(path, policy, result, ct));
                else
                {
                    ct.ThrowIfCancellationRequested();
                    result.SetResult(CreateBundleOrFallback(policy, path));
                }
            }
            catch (OperationCanceledException error) { result.SetException(error); }
            catch (Exception error) { CompleteProviderFailure(policy, error, result); }
            return result;
        }

        /// <summary>按当前平台/配置选择 Provider。</summary>
        public static IResourceProvider CreateProvider(ResourceFailurePolicy policy = ResourceFailurePolicy.BundleRequired, string manifestPath = null)
        {
#if UNITY_EDITOR
            if (!ForceBundleMode)
            {
                Log.Info("[Yato.Resources] 使用 EditorAssetProvider（不读 AB）");
                return new EditorAssetProvider();
            }

            Log.Info("[Yato.Resources] ForceBundleMode 已开启，编辑器将读取 AssetBundle");
#endif
            return CreateBundleOrFallback(policy, manifestPath);
        }

        private static IResourceProvider CreateBundleOrFallback(ResourceFailurePolicy policy, string manifestPath)
        {
            try
            {
                PreparePersistentCache();
                manifestPath = manifestPath ?? ResolveManifestPath();
                if (IsRemoteManifestPath(manifestPath))
                    throw new InvalidOperationException("StreamingAssets manifest is a URL; use ResourceServiceFactory.CreateAsync on Android/WebGL");
                ResourceManifest manifest = ResourceManifest.LoadFromFile(manifestPath);
                return CreateBundleProvider(manifest, manifestPath);
            }
            catch (Exception e)
            {
                var error = new ResourceException(null, null, ResourceStage.ManifestLoad,
                    string.Format("Provider=BundleProvider stage=ManifestLoad platform={0} manifest={1}: {2}",
                        RuntimePaths.PlatformName, manifestPath, e.Message), e);
                if (policy != ResourceFailurePolicy.AllowResourcesFallback) throw error;
                Log.Warn("[Yato.Resources] Explicit Resources fallback: " + error);
                return new ResourcesProvider();
            }
        }

        private static IResourceProvider CreateBundleProvider(ResourceManifest manifest, string manifestPath)
        {
            if (manifest.Platform != RuntimePaths.PlatformName)
                throw new FormatException("Manifest platform mismatch: " + manifest.Platform);
            foreach (var bundle in manifest.Bundles) manifest.ResolveLoadOrder(bundle.Key);

            BundleLoader loader = null;
            var refCounter = new BundleRefCounter(bundleName => loader?.Unload(bundleName));
            int lastSlash = manifestPath.LastIndexOf('/');
            string directory = IsRemoteManifestPath(manifestPath)
                ? manifestPath.Substring(0, lastSlash) : Path.GetDirectoryName(manifestPath);
            loader = new BundleLoader(refCounter, directory);
            Log.Info(string.Format("[Yato.Resources] 使用 BundleProvider：version={0} bundles={1} assets={2}",
                manifest.Version, manifest.BundleCount, manifest.AssetCount));
            return new BundleProvider(manifest, loader, refCounter);
        }

        internal static bool IsRemoteManifestPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith("jar:", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerator LoadManifestFromUrl(string path, ResourceFailurePolicy policy,
            AwaitableCompletionSource<IResourceProvider> result, CancellationToken ct)
        {
            // This URL is a Unity StreamingAssets address, not an arbitrary hot-update endpoint.
            using (var request = UnityWebRequest.Get(path))
            {
                request.timeout = 60;
                request.redirectLimit = 0;
                UnityWebRequestAsyncOperation operation = null;
                Exception startupError = null;
                try
                {
                    operation = request.SendWebRequest();
                }
                catch (Exception error) { startupError = error; }
                if (startupError != null)
                {
                    CompleteProviderFailure(policy, startupError, result);
                    yield break;
                }
                while (!operation.isDone)
                {
                    if (ct.IsCancellationRequested)
                    {
                        request.Abort();
                        result.SetException(new OperationCanceledException(ct));
                        yield break;
                    }
                    if (request.downloadedBytes > 4 * 1024 * 1024)
                    {
                        request.Abort();
                        CompleteProviderFailure(policy, new IOException("StreamingAssets manifest exceeds 4 MiB"), result);
                        yield break;
                    }
                    yield return null;
                }
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (request.result != UnityWebRequest.Result.Success)
                        throw new IOException("StreamingAssets manifest request failed: " + request.error);
                    if (request.downloadedBytes > 4 * 1024 * 1024)
                        throw new IOException("StreamingAssets manifest exceeds 4 MiB");
                    ct.ThrowIfCancellationRequested();
                    result.SetResult(CreateBundleProvider(ResourceManifest.Parse(request.downloadHandler.text, path), path));
                }
                catch (OperationCanceledException error) { result.SetException(error); }
                catch (Exception error) { CompleteProviderFailure(policy, error, result); }
            }
        }

        private static void CompleteProviderFailure(ResourceFailurePolicy policy, Exception error,
            AwaitableCompletionSource<IResourceProvider> result)
        {
            if (policy == ResourceFailurePolicy.AllowResourcesFallback)
            {
                Log.Warn("[Yato.Resources] Explicit Resources fallback: " + error);
                result.SetResult(new ResourcesProvider());
            }
            else result.SetException(error);
        }

        /// <summary>
        /// One attempt per session. Failed maintenance never blocks provider creation: the
        /// previous snapshot is still protected by the strict reader in HotUpdateStorage.
        /// </summary>
        private static void PreparePersistentCache()
        {
            if (!AutoCleanupPersistentCache || s_cachePrepared) return;
            if (!Application.isPlaying) return; // Edit-mode tooling must not delete a developer's cache.
            s_cachePrepared = true;
            try
            {
                CacheCleanupResult result = HotUpdateMaintenance.PrepareStartup();
                if (result.Success)
                {
                    Log.Info(string.Format(
                        "[Yato.Resources] 启动缓存维护：删除 {0} 个旧快照（{1} 字节），保留 {2} 个",
                        result.DeletedDirectories, result.DeletedBytes, result.RetainedDirectories));
                }
                else if (!result.Busy && !result.InUse)
                {
                    Log.Warn("[Yato.Resources] 启动缓存维护未完成（保留全部快照）：" + result.Error);
                }
            }
            catch (Exception e)
            {
                Log.Warn("[Yato.Resources] 启动缓存维护失败（保留全部快照）：" + e.Message);
            }
        }

        private static string ResolveManifestPath()
        {
            if (RuntimePaths.PreferPersistent)
            {
                string persistent = RuntimePaths.GetManifestPath(true);
                if (File.Exists(persistent))
                {
                    return persistent;
                }
            }

            return RuntimePaths.GetManifestPath(false);
        }
    }
}
