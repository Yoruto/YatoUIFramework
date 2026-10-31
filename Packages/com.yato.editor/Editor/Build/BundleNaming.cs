using System;
using System.Collections.Generic;

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
        public const string DefaultAssetRoot = "Assets/GameRes";

        /// <summary>参与打包的文件后缀（小写比较）。</summary>
        public static readonly string[] BuildableExtensions =
        {
            ".prefab", ".asset", ".mat", ".shader", ".compute",
            ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".exr",
            ".wav", ".mp3", ".ogg",
            ".txt", ".json", ".bytes", ".csv",
            ".anim", ".controller", ".spriteatlas", ".spriteatlasv2",
            ".unity", ".ttf", ".otf", ".shadervariants"
        };

        /// <summary>判断后缀是否需要打包。</summary>
        public static bool IsBuildable(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string ext = System.IO.Path.GetExtension(assetPath).ToLowerInvariant();
            for (int i = 0; i < BuildableExtensions.Length; i++)
            {
                if (BuildableExtensions[i] == ext)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>资源 key：资源根下的相对路径，小写、去扩展名、正斜杠。</summary>
        public static string GetAssetKey(string assetPath, string assetRoot)
        {
            string relative = ToRelative(assetPath, assetRoot);
            if (string.IsNullOrEmpty(relative))
            {
                return null;
            }

            int dot = relative.LastIndexOf('.');
            if (dot >= 0)
            {
                relative = relative.Substring(0, dot);
            }

            return relative.ToLowerInvariant();
        }

        /// <summary>按目录规则推导 bundle 名。</summary>
        public static string GetBundleName(string assetPath, string assetRoot)
        {
            string relative = ToRelative(assetPath, assetRoot);
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
            var result = new List<string>();
            if (!System.IO.Directory.Exists(assetRoot))
            {
                return result;
            }

            string[] files = System.IO.Directory.GetFiles(assetRoot, "*.*", System.IO.SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                if (!IsBuildable(path))
                {
                    continue;
                }

                if (path.Contains("/Editor/") || path.Contains("~/"))
                {
                    continue;
                }

                result.Add(path);
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static string ToRelative(string assetPath, string assetRoot)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            string normalizedPath = assetPath.Replace('\\', '/');
            string normalizedRoot = (assetRoot ?? DefaultAssetRoot).Replace('\\', '/').TrimEnd('/') + "/";

            if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return normalizedPath.Substring(normalizedRoot.Length);
        }

        private static string WithoutExtension(string fileName)
        {
            int dot = fileName.LastIndexOf('.');
            return dot >= 0 ? fileName.Substring(0, dot) : fileName;
        }
    }
}
