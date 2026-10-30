using System;
using UnityEngine;

namespace Yato.Resources
{
    /// <summary>
    /// 资源租约：唯一的释放凭据。Dispose 后引用计数递减且只执行一次（文档 3.3）。
    /// </summary>
    public interface IAssetHandle : IDisposable
    {
        /// <summary>资源对象；加载未完成或已释放时为 null。</summary>
        UnityEngine.Object Asset { get; }

        AssetKey Key { get; }

        bool IsDone { get; }

        /// <summary>句柄是否仍然有效（未释放且资源可用）。</summary>
        bool IsValid { get; }

        /// <summary>当前引用计数（诊断用）。</summary>
        int RefCount { get; }
    }

    /// <summary>强类型句柄。</summary>
    public interface IAssetHandle<T> : IAssetHandle where T : UnityEngine.Object
    {
        new T Asset { get; }
    }
}
