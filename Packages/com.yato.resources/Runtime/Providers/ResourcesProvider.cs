using System;
using System.Threading;
using UnityEngine;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>
    /// 降级后端：直接走 <see cref="Resources"/>，用于原型或 manifest 缺失时兜底（文档 3.5）。
    /// key 即 Resources 相对路径，例如 <c>ui/bag/bagform</c>。
    /// </summary>
    public sealed class ResourcesProvider : IResourceProvider
    {
        private readonly ResourceStats m_stats = new ResourceStats();

        public ResourceStats Stats
        {
            get { return m_stats; }
        }

        public IAwaitable<UnityEngine.Object> LoadAsync(AssetKey key, Type type, CancellationToken ct)
        {
            var source = new AwaitableCompletionSource<UnityEngine.Object>();

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
                UnityEngine.Object asset = UnityEngine.Resources.Load(key.Value, type);
                if (asset == null)
                {
                    throw new ResourceException(
                        key.ToString(), null, ResourceStage.AssetLoad,
                        "Resources 中不存在该资源（降级模式）");
                }

                m_stats.ActiveAssetCount++;
                source.SetResult(asset);
            }
            catch (Exception e)
            {
                source.SetException(e);
            }

            return source;
        }

        /// <summary>Resources 模式不做单资源释放，避免 <see cref="Resources.UnloadAsset"/> 误伤共享资源。</summary>
        public void Release(AssetKey key)
        {
        }

        public void ReleaseUnused()
        {
            // 只允许在场景切换 / 显式调用时触发（文档 3.3 第 4 条）
            UnityEngine.Resources.UnloadUnusedAssets();
            m_stats.ActiveAssetCount = 0;
            Log.Debug("[ResourcesProvider] 已执行 UnloadUnusedAssets");
        }
    }
}
