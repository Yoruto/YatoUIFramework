using System;
using System.Collections.Generic;
using System.IO;

namespace Yato.Resources.Editor
{
    /// <summary>Shared asset discovery and key rules for code generation and optional build tools.</summary>
    public static class AssetPathMapping
    {
        public const string DefaultAssetRoot = "Assets/GameRes";
        public static readonly string[] SupportedExtensions = AssetFileExtensions.Buildable;

        public static bool IsSupportedAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            string extension = Path.GetExtension(assetPath);
            for (int i = 0; i < SupportedExtensions.Length; i++)
                if (string.Equals(extension, SupportedExtensions[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string GetAssetKey(string assetPath, string assetRoot = DefaultAssetRoot)
        {
            string relative = GetRelativePath(assetPath, assetRoot);
            if (string.IsNullOrEmpty(relative)) return null;
            int dot = relative.LastIndexOf('.');
            if (dot >= 0) relative = relative.Substring(0, dot);
            return relative.ToLowerInvariant();
        }

        public static string GetRelativePath(string assetPath, string assetRoot = DefaultAssetRoot)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            string normalizedPath = assetPath.Replace('\\', '/');
            string normalizedRoot = (assetRoot ?? DefaultAssetRoot).Replace('\\', '/').TrimEnd('/') + "/";
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Substring(normalizedRoot.Length) : null;
        }

        /// <summary>Collect supported files in stable order, excluding Editor and ignored (~) directories.</summary>
        public static List<string> CollectAssets(string assetRoot = DefaultAssetRoot)
        {
            var result = new List<string>();
            if (!Directory.Exists(assetRoot)) return result;
            foreach (string file in Directory.GetFiles(assetRoot, "*.*", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                if (!IsSupportedAsset(path) || path.Contains("/Editor/") || path.Contains("~/")) continue;
                result.Add(path);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
