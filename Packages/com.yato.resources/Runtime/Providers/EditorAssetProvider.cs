using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Yato.Core;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Yato.Resources
{
    /// <summary>
    /// 编辑器后端：直接读 AssetDatabase，零打包即可打开任意页面（文档 6.3 / T-P2-02）。
    /// 仅在 <c>UNITY_EDITOR</c> 下生效；Player 构建中调用会明确报错。
    /// </summary>
    public sealed class EditorAssetProvider : IResourceProvider
    {
        private readonly ResourceStats m_stats = new ResourceStats();
        private readonly Dictionary<AssetKey, int> m_references = new Dictionary<AssetKey, int>();

        /// <summary>额外搜索根目录（相对 Assets），按顺序尝试。</summary>
        public readonly string[] SearchRoots;

        /// <summary>
        /// 为 true 时只接受约定路径命中，禁用叶子名兜底。用于验证 Editor 与 Player 的 key
        /// 行为一致；默认 false 以保持既有工程的开发便利。
        /// </summary>
        public bool StrictKeys { get; set; }

        public EditorAssetProvider(params string[] searchRoots)
            : this(false, searchRoots)
        {
        }

        public EditorAssetProvider(bool strictKeys, params string[] searchRoots)
        {
            StrictKeys = strictKeys;
            SearchRoots = searchRoots != null && searchRoots.Length > 0
                ? searchRoots
                : new[] { "Assets/GameRes", "Assets" };
        }

        public ResourceStats Stats
        {
            get { return m_stats; }
        }

        public IAwaitable<UnityEngine.Object> LoadAsync(AssetKey key, Type type, CancellationToken ct)
        {
            var source = new AwaitableCompletionSource<UnityEngine.Object>();

#if UNITY_EDITOR
            if (!key.IsValid)
            {
                source.SetException(new ResourceException(null, null, ResourceStage.KeyNormalize, "资源 key 非法"));
                return source;
            }

            if (ct.IsCancellationRequested)
            {
                source.SetException(new OperationCanceledException());
                return source;
            }

            try
            {
                string path = ResolvePath(key, type);
                if (string.IsNullOrEmpty(path))
                {
                    throw new ResourceException(
                        key.ToString(), null, ResourceStage.AssetLoad,
                        string.Format("AssetDatabase 中找不到资源（type={0}），搜索根：{1}",
                            type != null ? type.Name : "Object",
                            string.Join(", ", SearchRoots)));
                }

                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(path, type);
                if (asset == null)
                {
                    throw new ResourceException(
                        key.ToString(), null, ResourceStage.AssetLoad, "AssetDatabase 加载结果为空：" + path);
                }

                m_references.TryGetValue(key, out int references);
                m_references[key] = references + 1;
                m_stats.ActiveAssetCount = m_references.Count;
                Log.Debug(string.Format("[EditorAssetProvider] {0} -> {1}", key, path));
                source.SetResult(asset);
            }
            catch (Exception e)
            {
                source.SetException(e);
            }
#else
            source.SetException(new ResourceException(
                key.ToString(), null, ResourceStage.ProviderSelect,
                "EditorAssetProvider 仅在编辑器中可用"));
#endif
            return source;
        }

        public void Release(AssetKey key)
        {
            if (!m_references.TryGetValue(key, out int references)) return;
            if (references <= 1) m_references.Remove(key);
            else m_references[key] = references - 1;
            m_stats.ActiveAssetCount = m_references.Count;
        }

        public void ReleaseUnused()
        {
#if UNITY_EDITOR
            UnityEngine.Resources.UnloadUnusedAssets();
#endif
        }

#if UNITY_EDITOR
        private string ResolvePath(AssetKey key, Type type)
        {
            // 1) 按完整 key 与打包共用的扩展名解析，由 AssetDatabase 校验类型。
            string[] extensions = AssetFileExtensions.Buildable;
            for (int r = 0; r < SearchRoots.Length; r++)
            {
                string root = SearchRoots[r].TrimEnd('/');
                for (int e = 0; e < extensions.Length; e++)
                {
                    string candidate = string.Format("{0}/{1}{2}", root, key.Value, extensions[e]);
                    if (AssetDatabase.LoadAssetAtPath(candidate, type) != null)
                    {
                        return candidate;
                    }
                }
            }

            if (StrictKeys)
            {
                return null;
            }

            // 2) 兜底：按叶子名 + 类型过滤搜索（允许资源放在任意目录）。
            //    多个同名候选必须报错，否则 Editor 可能加载到与 Player 不同的资源。
            string leaf = key.Value;
            int slash = leaf.LastIndexOf('/');
            if (slash >= 0)
            {
                leaf = leaf.Substring(slash + 1);
            }

            string filter = type != null ? string.Format("{0} t:{1}", leaf, type.Name) : leaf;
            string[] guids = AssetDatabase.FindAssets(filter);
            var matches = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (string.Equals(fileName, leaf, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(path);
                }
            }

            if (matches.Count == 0)
            {
                return null;
            }

            matches.Sort(StringComparer.Ordinal);
            if (matches.Count > 1)
            {
                throw new ResourceException(key.ToString(), null, ResourceStage.AssetLoad,
                    string.Format("资源 key 未按约定路径命中，且叶子名存在 {0} 个候选：{1}。请使用完整 key 或调整目录。",
                        matches.Count, string.Join(", ", matches.ToArray())));
            }

            Log.Warn(string.Format(
                "[EditorAssetProvider] key 未按约定路径命中，已兜底为同名资源：{0} -> {1}。Player 不会执行该兜底。",
                key, matches[0]));
            return matches[0];
        }

#endif
    }
}
