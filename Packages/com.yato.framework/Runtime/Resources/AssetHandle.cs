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
        private readonly int m_refCount;
        private readonly Action<IAssetHandle> m_onDispose;

        private T m_asset;
        private int m_disposed;
        private readonly int m_trackId;

        public AssetHandle(AssetKey key, T asset, int refCount, Action<IAssetHandle> onDispose)
        {
            m_key = key;
            m_asset = asset;
            m_refCount = refCount < 1 ? 1 : refCount;
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
            get { return m_refCount; }
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
