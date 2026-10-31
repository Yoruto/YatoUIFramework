using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Yato.Resources.Internal;

namespace Yato.Resources
{
    /// <summary>
    /// 资源清单（schema v2，文档 3.4/6.4）。
    /// 由 Editor 的 ManifestBuilder 生成，运行时只读；禁止手工维护。
    /// </summary>
    public sealed class ResourceManifest
    {
        /// <summary>当前支持的 schema 版本。</summary>
        public const int CurrentSchema = 2;

        private readonly Dictionary<string, BundleInfo> m_bundles =
            new Dictionary<string, BundleInfo>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> m_assets =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public int Schema { get; private set; }

        public string Version { get; private set; }

        public string Platform { get; private set; }

        public DateTime BuildTime { get; private set; }

        public IReadOnlyDictionary<string, BundleInfo> Bundles
        {
            get { return m_bundles; }
        }

        /// <summary>资源 key → bundle 名。</summary>
        public IReadOnlyDictionary<string, string> Assets
        {
            get { return m_assets; }
        }

        public int BundleCount
        {
            get { return m_bundles.Count; }
        }

        public int AssetCount
        {
            get { return m_assets.Count; }
        }

        /// <summary>解析 manifest 文本。</summary>
        public static ResourceManifest Parse(string json, string source = null)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new ResourceException(null, null, ResourceStage.ManifestLoad,
                    string.Format("manifest 内容为空：{0}", source ?? "-"));
            }

            IDictionary<string, object> root;
            try
            {
                root = MiniJson.Deserialize(json) as IDictionary<string, object>;
            }
            catch (Exception e)
            {
                throw new ResourceException(null, null, ResourceStage.ManifestLoad,
                    string.Format("manifest JSON 解析失败：{0}", source ?? "-"), e);
            }

            if (root == null)
            {
                throw new ResourceException(null, null, ResourceStage.ManifestLoad,
                    string.Format("manifest 根节点不是对象：{0}", source ?? "-"));
            }

            var manifest = new ResourceManifest();
            manifest.Schema = (int)MiniJson.GetLong(root, "schema");
            if (manifest.Schema != CurrentSchema)
            {
                throw new ResourceException(null, null, ResourceStage.ManifestLoad,
                    string.Format("manifest schema 不受支持：{0}（当前 {1}），请重新打包", manifest.Schema, CurrentSchema));
            }

            manifest.Version = MiniJson.GetString(root, "version");
            manifest.Platform = MiniJson.GetString(root, "platform");

            string buildTime = MiniJson.GetString(root, "buildTime");
            DateTime parsedTime;
            if (!string.IsNullOrEmpty(buildTime) &&
                DateTime.TryParse(buildTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out parsedTime))
            {
                manifest.BuildTime = parsedTime;
            }

            ParseBundles(manifest, root, source);
            ParseAssets(manifest, root, source);
            return manifest;
        }

        /// <summary>从磁盘读取并解析。</summary>
        public static ResourceManifest LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ResourceException(null, null, ResourceStage.ManifestLoad, "manifest 路径为空");
            }

            if (!File.Exists(path))
            {
                throw new ResourceException(null, null, ResourceStage.ManifestLoad,
                    string.Format("manifest 不存在：{0}", path));
            }

            try
            {
                return Parse(File.ReadAllText(path), path);
            }
            catch (ResourceException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new ResourceException(null, null, ResourceStage.ManifestLoad,
                    string.Format("读取 manifest 失败：{0}", path), e);
            }
        }

        public bool ContainsAsset(string assetKey)
        {
            return !string.IsNullOrEmpty(assetKey) && m_assets.ContainsKey(NormalizeKey(assetKey));
        }

        /// <summary>查询资源所属 bundle。</summary>
        public bool TryGetBundleOfAsset(string assetKey, out BundleInfo bundle)
        {
            bundle = null;
            if (string.IsNullOrEmpty(assetKey))
            {
                return false;
            }

            string bundleName;
            if (!m_assets.TryGetValue(NormalizeKey(assetKey), out bundleName))
            {
                return false;
            }

            return m_bundles.TryGetValue(bundleName, out bundle);
        }

        public BundleInfo GetBundle(string bundleName)
        {
            BundleInfo bundle;
            return !string.IsNullOrEmpty(bundleName) && m_bundles.TryGetValue(bundleName, out bundle) ? bundle : null;
        }

        /// <summary>
        /// 展开某个 bundle 的完整依赖链：依赖在前、自身在最后；检测到环时抛异常（文档 3.3/8.1）。
        /// </summary>
        public List<BundleInfo> ResolveLoadOrder(string bundleName)
        {
            var order = new List<BundleInfo>(8);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            VisitBundle(bundleName, order, visiting, visited);
            return order;
        }

        private void VisitBundle(string bundleName, List<BundleInfo> order, HashSet<string> visiting, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(bundleName) || visited.Contains(bundleName))
            {
                return;
            }

            if (!visiting.Add(bundleName))
            {
                throw new ResourceException(null, bundleName, ResourceStage.BundleLoad,
                    string.Format("bundle 依赖存在循环：{0}", bundleName));
            }

            BundleInfo bundle;
            if (!m_bundles.TryGetValue(bundleName, out bundle))
            {
                throw new ResourceException(null, bundleName, ResourceStage.BundleLoad,
                    string.Format("manifest 缺少 bundle：{0}", bundleName));
            }

            for (int i = 0; i < bundle.Dependencies.Count; i++)
            {
                VisitBundle(bundle.Dependencies[i], order, visiting, visited);
            }

            visiting.Remove(bundleName);
            visited.Add(bundleName);
            order.Add(bundle);
        }

        private static void ParseBundles(ResourceManifest manifest, IDictionary<string, object> root, string source)
        {
            object rawBundles;
            if (!root.TryGetValue("bundles", out rawBundles) || rawBundles == null)
            {
                throw new ResourceException(null, null, ResourceStage.ManifestLoad,
                    string.Format("manifest 缺少 bundles：{0}", source ?? "-"));
            }

            var bundles = rawBundles as IDictionary<string, object>;
            if (bundles == null)
            {
                throw new ResourceException(null, null, ResourceStage.ManifestLoad,
                    string.Format("manifest.bundles 不是对象：{0}", source ?? "-"));
            }

            foreach (KeyValuePair<string, object> pair in bundles)
            {
                var data = pair.Value as IDictionary<string, object>;
                if (data == null)
                {
                    throw new ResourceException(null, pair.Key, ResourceStage.ManifestLoad,
                        "bundle 条目不是对象");
                }

                var info = new BundleInfo
                {
                    Name = pair.Key,
                    Hash = MiniJson.GetString(data, "hash"),
                    Crc = (uint)MiniJson.GetLong(data, "crc"),
                    Size = MiniJson.GetLong(data, "size"),
                    Compression = MiniJson.GetString(data, "compression"),
                    Dependencies = MiniJson.GetStringList(data, "deps"),
                    Assets = MiniJson.GetStringList(data, "assets")
                };

                manifest.m_bundles[pair.Key] = info;
            }
        }

        private static void ParseAssets(ResourceManifest manifest, IDictionary<string, object> root, string source)
        {
            object rawAssets;
            if (!root.TryGetValue("assets", out rawAssets) || rawAssets == null)
            {
                throw new ResourceException(null, null, ResourceStage.ManifestLoad,
                    string.Format("manifest 缺少 assets：{0}", source ?? "-"));
            }

            var assets = rawAssets as IDictionary<string, object>;
            if (assets == null)
            {
                throw new ResourceException(null, null, ResourceStage.ManifestLoad,
                    string.Format("manifest.assets 不是对象：{0}", source ?? "-"));
            }

            foreach (KeyValuePair<string, object> pair in assets)
            {
                string bundleName = pair.Value as string;
                if (string.IsNullOrEmpty(bundleName))
                {
                    throw new ResourceException(pair.Key, null, ResourceStage.ManifestLoad,
                        "asset 映射的 bundle 名为空");
                }

                if (!manifest.m_bundles.ContainsKey(bundleName))
                {
                    throw new ResourceException(pair.Key, bundleName, ResourceStage.ManifestLoad,
                        "asset 指向了不存在的 bundle");
                }

                manifest.m_assets[NormalizeKey(pair.Key)] = bundleName;
            }
        }

        private static string NormalizeKey(string key)
        {
            return AssetKey.Normalize(key);
        }

        /// <summary>单个 bundle 的清单信息。</summary>
        public sealed class BundleInfo
        {
            public string Name { get; internal set; }

            public string Hash { get; internal set; }

            public uint Crc { get; internal set; }

            public long Size { get; internal set; }

            public string Compression { get; internal set; }

            /// <summary>依赖的 bundle 名。</summary>
            public List<string> Dependencies { get; internal set; }

            /// <summary>该 bundle 内的资源 key。</summary>
            public List<string> Assets { get; internal set; }

            public override string ToString()
            {
                return string.Format("{0} size={1} deps={2}", Name, Size, Dependencies != null ? Dependencies.Count : 0);
            }
        }
    }
}
