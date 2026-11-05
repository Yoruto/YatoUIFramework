using System;
using System.Collections.Generic;
using System.IO;
using Yato.Resources.Internal;

namespace Yato.Resources
{
    /// <summary>
    /// 远端版本文件（version.json，T-P5-01）。
    /// <code>
    /// { "schema":1, "appVersion":"1.0.0", "platform":"Windows",
    ///   "manifestCrc":123, "bundles": { "ui/bag": {"hash":"..","crc":1,"size":1024} } }
    /// </code>
    /// </summary>
    public sealed class VersionManifest
    {
        public const int CurrentSchema = 1;
        public const int MaxBundleCount = 10000;
        public const long MaxBundleBytes = 1024L * 1024 * 1024;
        public const long MaxTotalBytes = 8L * 1024 * 1024 * 1024;

        private readonly Dictionary<string, BundleHash> m_bundles =
            new Dictionary<string, BundleHash>(StringComparer.Ordinal);

        public int Schema { get; private set; }

        public string AppVersion { get; private set; }

        public string Platform { get; private set; }

        /// <summary>manifest.json 的 CRC（下载后校验用）。</summary>
        public uint ManifestCrc { get; private set; }

        public IReadOnlyDictionary<string, BundleHash> Bundles
        {
            get { return m_bundles; }
        }

        public sealed class BundleHash
        {
            public string Hash;
            public uint Crc;
            public long Size;

            /// <summary>
            /// True when the publisher supplied a usable SHA-256 digest (64 hexadecimal characters).
            /// Shorter legacy placeholders are ignored so existing manifests keep working; a real
            /// digest is verified by the downloader and by snapshot revalidation.
            /// </summary>
            public bool HasSha256
            {
                get { return IsSha256(Hash); }
            }

            public static bool IsSha256(string value)
            {
                if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                    if (!hex) return false;
                }

                return true;
            }
        }

        public static VersionManifest Parse(string json, string source = null)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new ResourceException(null, null, ResourceStage.BundleDownload,
                    "version.json 内容为空：" + (source ?? "-"));
            }

            IDictionary<string, object> root;
            try
            {
                root = MiniJson.Deserialize(json) as IDictionary<string, object>;
            }
            catch (Exception e)
            {
                throw new ResourceException(null, null, ResourceStage.BundleDownload, "version.json 解析失败", e);
            }

            if (root == null)
            {
                throw new ResourceException(null, null, ResourceStage.BundleDownload, "version.json 根节点非法");
            }

            var manifest = new VersionManifest();
            manifest.Schema = (int)MiniJson.RequireInteger(root, "schema", int.MaxValue);
            if (manifest.Schema != CurrentSchema)
            {
                throw new ResourceException(null, null, ResourceStage.BundleDownload,
                    string.Format("version schema 不受支持：{0}", manifest.Schema));
            }

            manifest.AppVersion = MiniJson.GetString(root, "appVersion");
            manifest.Platform = MiniJson.GetString(root, "platform");
            manifest.ManifestCrc = (uint)MiniJson.RequireInteger(root, "manifestCrc", uint.MaxValue);
            if (string.IsNullOrWhiteSpace(manifest.AppVersion) || manifest.AppVersion.Length > 128)
                throw new FormatException("Invalid appVersion");
            if (!string.Equals(manifest.Platform, RuntimePaths.PlatformName, StringComparison.Ordinal))
                throw new FormatException("Update platform mismatch: " + manifest.Platform + " != " + RuntimePaths.PlatformName);

            object rawBundles;
            if (!root.TryGetValue("bundles", out rawBundles) || !(rawBundles is IDictionary<string, object>))
                throw new FormatException("version.bundles must be an object");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            if (root.TryGetValue("bundles", out rawBundles))
            {
                var bundles = rawBundles as IDictionary<string, object>;
                if (bundles != null)
                {
                    if (bundles.Count > MaxBundleCount) throw new FormatException("Too many bundles");
                    foreach (KeyValuePair<string, object> pair in bundles)
                    {
                        BundleRelativePath.ValidateBundle(pair.Key);
                        if (!names.Add(pair.Key)) throw new FormatException("Duplicate portable bundle path: " + pair.Key);
                        var data = pair.Value as IDictionary<string, object>;
                        if (data == null)
                        {
                            throw new FormatException("Invalid bundle entry: " + pair.Key);
                        }

                        string hash = MiniJson.GetString(data, "hash");
                        if (!string.IsNullOrEmpty(hash) && hash.Length > 128)
                            throw new FormatException("Invalid bundle hash: " + pair.Key);
                        manifest.m_bundles[pair.Key] = new BundleHash
                        {
                            Hash = hash,
                            Crc = (uint)MiniJson.RequireInteger(data, "crc", uint.MaxValue),
                            Size = MiniJson.RequireInteger(data, "size", MaxBundleBytes)
                        };
                        total += manifest.m_bundles[pair.Key].Size;
                        if (total > MaxTotalBytes) throw new FormatException("Update size limit exceeded");
                    }
                }
            }

            foreach (string name in names)
            {
                int separator = name.IndexOf('/');
                while (separator >= 0)
                {
                    if (names.Contains(name.Substring(0, separator))) throw new FormatException("Bundle file/directory collision: " + name);
                    separator = name.IndexOf('/', separator + 1);
                }
            }

            return manifest;
        }

        public static VersionManifest LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new ResourceException(null, null, ResourceStage.BundleDownload, "version.json 不存在：" + path);
            }

            return Parse(File.ReadAllText(path), path);
        }
    }
}
