using System;
using System.Collections.Generic;
using Yato.Resources.Editor;

namespace Yato.Editor
{
    /// <summary>
    /// 资源路径 → (AssetKey, BundleName) 的唯一映射，禁止手填 bundle 名（文档 2.4 / 6.4）。
    /// 规则示例：
    /// <code>
    /// Assets/GameRes/UI/Bag/BagForm.prefab → key=ui/bag/bagform, bundle=ui/bag
    /// Assets/GameRes/Scene/Main.unity      → key=scene/main,      bundle=scene/main
    /// Assets/GameRes/Shader/UI.shader      → key=shader/ui,       bundle=shader/common
    /// Assets/GameRes/Config/item.json      → key=config/item,     bundle=config
    /// Assets/GameRes/Audio/Bgm/bgm_01.wav  → key=audio/bgm/bgm_01,bundle=audio/bgm
    /// </code>
    /// </summary>
    public static class BundleNaming
    {
        /// <summary>默认资源根目录（不含尾部斜杠）。</summary>
        public const string DefaultAssetRoot = AssetPathMapping.DefaultAssetRoot;

        /// <summary>参与打包的文件后缀（小写比较）。</summary>
        public static readonly string[] BuildableExtensions = AssetPathMapping.SupportedExtensions;

        /// <summary>判断后缀是否需要打包。</summary>
        public static bool IsBuildable(string assetPath)
        {
            return AssetPathMapping.IsSupportedAsset(assetPath);
        }

        /// <summary>资源 key：资源根下的相对路径，小写、去扩展名、正斜杠。</summary>
        public static string GetAssetKey(string assetPath, string assetRoot)
        {
            return AssetPathMapping.GetAssetKey(assetPath, assetRoot);
        }

        /// <summary>按目录规则推导 bundle 名。</summary>
        public static string GetBundleName(string assetPath, string assetRoot)
        {
            string relative = AssetPathMapping.GetRelativePath(assetPath, assetRoot);
            if (string.IsNullOrEmpty(relative))
            {
                return null;
            }

            string[] segments = relative.Split('/');
            string top = segments[0].ToLowerInvariant();

            switch (top)
            {
                case "ui":
                    return segments.Length >= 3
                        ? string.Format("ui/{0}", segments[1].ToLowerInvariant())
                        : "ui/common";

                case "scene":
                    return string.Format("scene/{0}", WithoutExtension(segments[segments.Length - 1]).ToLowerInvariant());

                case "shader":
                    return "shader/common";

                case "config":
                    return "config";

                case "audio":
                    return segments.Length >= 3
                        ? string.Format("audio/{0}", segments[1].ToLowerInvariant())
                        : "audio/default";

                case "font":
                case "fonts":
                    return "font/common";

                default:
                    return string.Format("assets/{0}", top);
            }
        }

        /// <summary>收集需要打包的资源（按后缀过滤，跳过 Editor 目录与 ~ 结尾目录）。</summary>
        public static List<string> CollectBuildableAssets(string assetRoot)
        {
            return AssetPathMapping.CollectAssets(assetRoot);
        }

        private static string WithoutExtension(string fileName)
        {
            int dot = fileName.LastIndexOf('.');
            return dot >= 0 ? fileName.Substring(0, dot) : fileName;
        }
    }
}
