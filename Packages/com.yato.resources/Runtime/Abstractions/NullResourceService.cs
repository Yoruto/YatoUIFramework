using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Yato.Resources
{
    /// <summary>
    /// 空实现：UI 在未注入资源服务时仍可编译与运行（页面打开会明确失败并给出原因），
    /// 避免 Yato.UI 反向依赖具体资源实现（T-P1-09）。
    /// </summary>
    public sealed class NullResourceService : IResourceService
    {
        public static readonly NullResourceService Instance = new NullResourceService();

        public ResourceStats Stats
        {
            get { return ResourceStats.Empty; }
        }

        public IAssetHandle<T> Load<T>(AssetKey key) where T : UnityEngine.Object
        {
            throw CreateException(key, "同步加载");
        }

        public IAwaitable<IAssetHandle<T>> LoadAsync<T>(AssetKey key, CancellationToken ct = default(CancellationToken))
            where T : UnityEngine.Object
        {
            var source = new AwaitableCompletionSource<IAssetHandle<T>>();
            source.SetException(CreateException(key, "异步加载"));
            return source;
        }

        public IAwaitable<IAssetHandle<GameObject>> InstantiateAsync(
            AssetKey key,
            Transform parent,
            CancellationToken ct = default(CancellationToken))
        {
            var source = new AwaitableCompletionSource<IAssetHandle<GameObject>>();
            source.SetException(CreateException(key, "实例化"));
            return source;
        }

        public IAwaitable PreloadAsync(
            IReadOnlyList<AssetKey> keys,
            IProgress<float> progress = null,
            CancellationToken ct = default(CancellationToken))
        {
            var source = new AwaitableCompletionSource();
            source.SetException(new ResourceException(null, null, ResourceStage.ProviderSelect, "未注入 IResourceService，预加载不可用"));
            return source;
        }

        public void Release(IAssetHandle handle)
        {
        }

        public void ReleaseUnused()
        {
        }

        private static ResourceException CreateException(AssetKey key, string stage)
        {
            return new ResourceException(
                key.ToString(),
                null,
                ResourceStage.ProviderSelect,
                string.Format("未注入 IResourceService，无法完成{0}；请在启动时注册资源服务", stage));
        }
    }
}
