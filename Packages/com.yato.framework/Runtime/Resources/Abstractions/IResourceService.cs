using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Yato.Resources
{
    /// <summary>UI 与业务唯一依赖的资源入口（文档 3.2）。</summary>
    public interface IResourceService
    {
        /// <summary>同步加载；已缓存时立即返回。正式包中应尽量避免使用（RES-12）。</summary>
        IAssetHandle<T> Load<T>(AssetKey key) where T : UnityEngine.Object;

        IAwaitable<IAssetHandle<T>> LoadAsync<T>(AssetKey key, CancellationToken ct = default(CancellationToken))
            where T : UnityEngine.Object;

        IAwaitable<IAssetHandle<GameObject>> InstantiateAsync(
            AssetKey key,
            Transform parent,
            CancellationToken ct = default(CancellationToken));

        IAwaitable PreloadAsync(
            IReadOnlyList<AssetKey> keys,
            IProgress<float> progress = null,
            CancellationToken ct = default(CancellationToken));

        /// <summary>释放句柄（幂等）。</summary>
        void Release(IAssetHandle handle);

        /// <summary>立即清理引用归零的 bundle（默认由内部延迟队列处理）。</summary>
        void ReleaseUnused();

        ResourceStats Stats { get; }
    }

    /// <summary>具体加载后端（Editor / Resources / Bundle / Addressables）。</summary>
    public interface IResourceProvider
    {
        IAwaitable<UnityEngine.Object> LoadAsync(AssetKey key, Type type, CancellationToken ct);

        void Release(AssetKey key);

        void ReleaseUnused();

        ResourceStats Stats { get; }
    }
}
