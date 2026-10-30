using System;
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

        /// <summary>额外搜索根目录（相对 Assets），按顺序尝试。</summary>
        public readonly string[] SearchRoots;

        public EditorAssetProvider(params string[] searchRoots)
        {
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

                m_stats.ActiveAssetCount++;
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
            // 1) 按约定路径 + 常见扩展名直接命中
            string[] extensions = GetExtensions(type);
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

            // 2) 兜底：按叶子名 + 类型过滤搜索（允许资源放在任意目录）
            string leaf = key.Value;
            int slash = leaf.LastIndexOf('/');
            if (slash >= 0)
            {
                leaf = leaf.Substring(slash + 1);
            }

            string filter = type != null ? string.Format("{0} t:{1}", leaf, type.Name) : leaf;
            string[] guids = AssetDatabase.FindAssets(filter);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (string.Equals(fileName, leaf, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return null;
        }

        private static string[] GetExtensions(Type type)
        {
            if (type == typeof(GameObject))
            {
                return new[] { ".prefab", ".asset" };
            }

            if (type == typeof(Sprite))
            {
                return new[] { ".png", ".jpg", ".psd", ".asset" };
            }

            if (type == typeof(Texture) || type == typeof(Texture2D))
            {
                return new[] { ".png", ".jpg", ".tga", ".asset" };
            }

            if (type == typeof(Material))
            {
                return new[] { ".mat" };
            }

            if (type == typeof(AudioClip))
            {
                return new[] { ".wav", ".mp3", ".ogg" };
            }

            return new[] { ".asset", ".prefab" };
        }
#endif
    }
}
