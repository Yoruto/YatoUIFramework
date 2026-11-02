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

        /// <summary>最近一次创建的服务实例（供诊断窗口读取统计）。</summary>
        public static IResourceService Current { get; private set; }

        /// <summary>
        /// 关闭 Domain Reload 时静态状态不会自动清空，必须显式重置，
        /// 否则诊断窗口会读到上一会话的 service（对应文档 D02）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            ForceBundleMode = false;
        }

        /// <summary>创建默认资源服务。</summary>
        public static IResourceService Create(IResourceProvider provider = null)
        {
            var service = new ResourceService(provider ?? CreateProvider());
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
