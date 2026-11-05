using System;
using System.IO;
using UnityEngine;
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
                ResourceManifest manifest = ResourceManifest.LoadFromFile(manifestPath);
                if (manifest.Platform != RuntimePaths.PlatformName)
                    throw new FormatException("Manifest platform mismatch: " + manifest.Platform);
                foreach (var bundle in manifest.Bundles) manifest.ResolveLoadOrder(bundle.Key);

                BundleLoader loader = null;
                var refCounter = new BundleRefCounter(delegate(string bundleName)
                {
                    if (loader != null)
                    {
                        loader.Unload(bundleName);
                    }
                });

                loader = new BundleLoader(refCounter, Path.GetDirectoryName(manifestPath));

                Log.Info(string.Format(
                    "[Yato.Resources] 使用 BundleProvider：version={0} bundles={1} assets={2}",
                    manifest.Version, manifest.BundleCount, manifest.AssetCount));

                return new BundleProvider(manifest, loader, refCounter);
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
