using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Yato.Resources;

namespace Yato.Editor
{
    /// <summary>
    /// 把 Unity 的 <see cref="AssetBundleManifest"/> + 打包分组转换成运行时使用的
    /// <c>manifest.json</c>（schema v2，文档 3.4/6.4）。
    /// </summary>
    public static class ManifestBuilder
    {
        /// <summary>参与打包的一组资源。</summary>
        public sealed class BundleAssetGroup
        {
            public string BundleName;
            public List<string> AssetPaths = new List<string>();
            public List<string> AssetKeys = new List<string>();
        }

        /// <summary>写入 manifest.json，返回文件路径。</summary>
        public static string Write(
            AssetBundleManifest unityManifest,
            IList<BundleAssetGroup> groups,
            string outputDir,
            string platformName,
            string version)
        {
            if (unityManifest == null)
            {
                throw new InvalidOperationException("AssetBundleManifest 为空，打包可能失败");
            }

            if (groups == null || groups.Count == 0)
            {
                throw new InvalidOperationException("没有可写入 manifest 的 bundle");
            }

            // bundle 名 → 依赖（过滤掉 Unity 自动生成的平台 manifest bundle）
            var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var sortedGroups = new List<BundleAssetGroup>(groups);
            sortedGroups.Sort(delegate(BundleAssetGroup a, BundleAssetGroup b)
            {
                return string.CompareOrdinal(a.BundleName, b.BundleName);
            });

            var assetToBundle = new SortedDictionary<string, string>(StringComparer.Ordinal);

            for (int i = 0; i < sortedGroups.Count; i++)
            {
                BundleAssetGroup group = sortedGroups[i];
                string[] directDeps = unityManifest.GetDirectDependencies(group.BundleName);
                var list = new List<string>();
                if (directDeps != null)
                {
                    for (int d = 0; d < directDeps.Length; d++)
                    {
                        if (!string.IsNullOrEmpty(directDeps[d]))
                        {
                            list.Add(directDeps[d]);
                        }
                    }
                }

                list.Sort(StringComparer.Ordinal);
                deps[group.BundleName] = list;

                List<string> keys = group.AssetKeys;
                keys.Sort(StringComparer.Ordinal);
                for (int k = 0; k < keys.Count; k++)
                {
                    assetToBundle[keys[k]] = group.BundleName;
                }
            }

            var builder = new StringBuilder(4096);
            builder.Append("{\n");
            builder.Append("  \"schema\": ").Append(ResourceManifest.CurrentSchema).Append(",\n");
            builder.Append("  \"version\": \"").Append(Escape(version ?? "0.0.0")).Append("\",\n");
            builder.Append("  \"buildTime\": \"").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append("\",\n");
            builder.Append("  \"platform\": \"").Append(Escape(platformName)).Append("\",\n");

            builder.Append("  \"bundles\": {\n");
            for (int i = 0; i < sortedGroups.Count; i++)
            {
                BundleAssetGroup group = sortedGroups[i];
                string filePath = Path.Combine(outputDir, group.BundleName);
                long size = File.Exists(filePath) ? new FileInfo(filePath).Length : 0L;
                uint crc = File.Exists(filePath) ? Crc32.Compute(filePath) : 0u;
                string hash = unityManifest.GetAssetBundleHash(group.BundleName).ToString();

                builder.Append("    \"").Append(Escape(group.BundleName)).Append("\": {\n");
                builder.Append("      \"hash\": \"").Append(Escape(hash)).Append("\",\n");
                builder.Append("      \"crc\": ").Append(crc).Append(",\n");
                builder.Append("      \"size\": ").Append(size).Append(",\n");
                builder.Append("      \"compression\": \"LZ4\",\n");
                AppendStringArray(builder, "deps", deps[group.BundleName]);
                builder.Append(",\n");
                AppendStringArray(builder, "assets", group.AssetKeys);
                builder.Append("\n    }");
                builder.Append(i == sortedGroups.Count - 1 ? "\n" : ",\n");
            }

            builder.Append("  },\n");
            builder.Append("  \"assets\": {\n");

            int index = 0;
            foreach (KeyValuePair<string, string> pair in assetToBundle)
            {
                builder.Append("    \"").Append(Escape(pair.Key)).Append("\": \"")
                    .Append(Escape(pair.Value)).Append("\"");
                builder.Append(++index == assetToBundle.Count ? "\n" : ",\n");
            }

            builder.Append("  }\n}\n");

            string manifestPath = Path.Combine(outputDir, RuntimePaths.ManifestFileName);
            File.WriteAllText(manifestPath, builder.ToString(), new UTF8Encoding(false));

            WriteVersionFile(manifestPath, outputDir, platformName, version, sortedGroups);
            return manifestPath;
        }

        /// <summary>生成 version.json（热更比对用）：manifest CRC + 每个 bundle 的 hash/crc/size。</summary>
        private static void WriteVersionFile(
            string manifestPath,
            string outputDir,
            string platformName,
            string version,
            IList<BundleAssetGroup> groups)
        {
            var builder = new StringBuilder(1024);
            builder.Append("{\n");
            builder.Append("  \"schema\": 1,\n");
            builder.Append("  \"appVersion\": \"").Append(Escape(version ?? "0.0.0")).Append("\",\n");
            builder.Append("  \"platform\": \"").Append(Escape(platformName)).Append("\",\n");
            builder.Append("  \"manifestCrc\": ").Append(Crc32.Compute(manifestPath)).Append(",\n");
            builder.Append("  \"bundles\": {\n");

            for (int i = 0; i < groups.Count; i++)
            {
                BundleAssetGroup group = groups[i];
                string filePath = Path.Combine(outputDir, group.BundleName);
                long size = File.Exists(filePath) ? new FileInfo(filePath).Length : 0L;
                uint crc = File.Exists(filePath) ? Crc32.Compute(filePath) : 0u;

                builder.Append("    \"").Append(Escape(group.BundleName)).Append("\": { \"crc\": ")
                    .Append(crc).Append(", \"size\": ").Append(size).Append(" }");
                builder.Append(i == groups.Count - 1 ? "\n" : ",\n");
            }

            builder.Append("  }\n}\n");
            File.WriteAllText(
                Path.Combine(outputDir, RuntimePaths.VersionFileName),
                builder.ToString(),
                new UTF8Encoding(false));
        }

        private static void AppendStringArray(StringBuilder builder, string key, IList<string> values)
        {
            builder.Append("      \"").Append(key).Append("\": [");
            if (values != null && values.Count > 0)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append('"').Append(Escape(values[i])).Append('"');
                }
            }

            builder.Append("]");
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    /// <summary>标准 CRC32（用于热更校验，P5 会复用同一算法）。</summary>
    public static class Crc32
    {
        private static uint[] s_table;

        public static uint Compute(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            return Compute(bytes, 0, bytes.Length);
        }

        public static uint Compute(byte[] buffer, int offset, int count)
        {
            uint[] table = GetTable();
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < count; i++)
            {
                crc = (crc >> 8) ^ table[(crc ^ buffer[offset + i]) & 0xFF];
            }

            return ~crc;
        }

        private static uint[] GetTable()
        {
            if (s_table != null)
            {
                return s_table;
            }

            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                }

                table[i] = value;
            }

            s_table = table;
            return s_table;
        }
    }
}
