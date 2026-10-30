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
            manifest.Schema = (int)MiniJson.GetLong(root, "schema");
            if (manifest.Schema != CurrentSchema)
            {
                throw new ResourceException(null, null, ResourceStage.BundleDownload,
                    string.Format("version schema 不受支持：{0}", manifest.Schema));
            }

            manifest.AppVersion = MiniJson.GetString(root, "appVersion");
            manifest.Platform = MiniJson.GetString(root, "platform");
            manifest.ManifestCrc = (uint)MiniJson.GetLong(root, "manifestCrc");

            object rawBundles;
            if (root.TryGetValue("bundles", out rawBundles))
            {
                var bundles = rawBundles as IDictionary<string, object>;
                if (bundles != null)
                {
                    foreach (KeyValuePair<string, object> pair in bundles)
                    {
                        var data = pair.Value as IDictionary<string, object>;
                        if (data == null)
                        {
                            continue;
                        }

                        manifest.m_bundles[pair.Key] = new BundleHash
                        {
                            Hash = MiniJson.GetString(data, "hash"),
                            Crc = (uint)MiniJson.GetLong(data, "crc"),
                            Size = MiniJson.GetLong(data, "size")
                        };
                    }
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
