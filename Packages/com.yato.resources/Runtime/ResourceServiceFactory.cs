using System;
using System.IO;
using UnityEngine;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>
    /// 运行时 Provider 选择（文档 3.5）：
    /// 编辑器默认 AssetDatabase；强制 Bundle 或 Player 下按 manifest 选择 BundleProvider，
    /// manifest 缺失时降级 ResourcesProvider 并告警。
    /// </summary>
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
        public static IResourceProvider CreateProvider()
        {
#if UNITY_EDITOR
            if (!ForceBundleMode)
            {
                Log.Info("[Yato.Resources] 使用 EditorAssetProvider（不读 AB）");
                return new EditorAssetProvider();
            }

            Log.Info("[Yato.Resources] ForceBundleMode 已开启，编辑器将读取 AssetBundle");
#endif
            return CreateBundleOrFallback();
        }

        private static IResourceProvider CreateBundleOrFallback()
        {
            try
            {
                string manifestPath = ResolveManifestPath();
                ResourceManifest manifest = ResourceManifest.LoadFromFile(manifestPath);

                BundleLoader loader = null;
                var refCounter = new BundleRefCounter(delegate(string bundleName)
                {
                    if (loader != null)
                    {
                        loader.Unload(bundleName);
                    }
                });

                loader = new BundleLoader(refCounter);

                Log.Info(string.Format(
                    "[Yato.Resources] 使用 BundleProvider：version={0} bundles={1} assets={2}",
                    manifest.Version, manifest.BundleCount, manifest.AssetCount));

                return new BundleProvider(manifest, loader, refCounter);
            }
            catch (Exception e)
            {
                Log.Warn(string.Format(
                    "[Yato.Resources] manifest 不可用，降级为 ResourcesProvider：{0}",
                    e.Message));
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
