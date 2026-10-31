using System;
using System.Threading;

namespace Yato.Resources
{
    /// <summary>
    /// 资源句柄默认实现：Dispose 只执行一次，随后回调释放逻辑（文档 3.3 第 2 条）。
    /// </summary>
    internal sealed class AssetHandle<T> : IAssetHandle<T> where T : UnityEngine.Object
    {
        private readonly AssetKey m_key;
        private readonly Func<int> m_refCountProvider;
        private readonly int m_fixedRefCount;
        private readonly Action<IAssetHandle> m_onDispose;

        private T m_asset;
        private int m_disposed;
        private readonly int m_trackId;

        public AssetHandle(AssetKey key, T asset, int refCount, Action<IAssetHandle> onDispose)
            : this(key, asset, null, refCount, onDispose)
        {
        }

        /// <summary>
        /// 使用实时的引用计数读取器：RefCount 反映当前真实持有者数量，而不是创建时的快照。
        /// </summary>
        public AssetHandle(AssetKey key, T asset, Func<int> refCountProvider, Action<IAssetHandle> onDispose)
            : this(key, asset, refCountProvider, 1, onDispose)
        {
        }

        private AssetHandle(
            AssetKey key,
            T asset,
            Func<int> refCountProvider,
            int refCount,
            Action<IAssetHandle> onDispose)
        {
            m_key = key;
            m_asset = asset;
            m_refCountProvider = refCountProvider;
            m_fixedRefCount = refCount < 1 ? 1 : refCount;
            m_onDispose = onDispose;
            m_trackId = ResourceTracker.Track(key.ToString());
        }

        public T Asset
        {
            get { return m_disposed == 0 ? m_asset : null; }
        }

        UnityEngine.Object IAssetHandle.Asset
        {
            get { return Asset; }
        }

        public AssetKey Key
        {
            get { return m_key; }
        }

        public bool IsDone
        {
            get { return true; }
        }

        public bool IsValid
        {
            get { return m_disposed == 0 && m_asset != null; }
        }

        public int RefCount
        {
            get
            {
                if (m_refCountProvider != null)
                {
                    try
                    {
                        return m_refCountProvider();
                    }
                    catch (Exception)
                    {
                        return 0;
                    }
                }

                return m_fixedRefCount;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            m_asset = null;
            ResourceTracker.Untrack(m_trackId);

            Action<IAssetHandle> callback = m_onDispose;
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(this);
            }
            catch (Exception e)
            {
                Yato.Core.Log.Error("AssetHandle", string.Format("释放资源失败：{0}", m_key), e);
            }
        }
    }
}
