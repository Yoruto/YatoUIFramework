using System;
using UnityEngine;

namespace Yato.Resources
{
    /// <summary>已加载资源的运行时记录（文档 3.3 的 AssetEntry）。</summary>
    public sealed class AssetEntry
    {
        public AssetKey Key;

        public UnityEngine.Object Asset;

        public Type AssetType;

        /// <summary>句柄引用计数；归零后由 Provider 释放。</summary>
        public int RefCount;

        public bool IsLoaded
        {
            get { return Asset != null; }
        }

        public int AddRef()
        {
            return ++RefCount;
        }

        /// <summary>递减并返回剩余计数（不会小于 0）。</summary>
        public int Release()
        {
            if (RefCount > 0)
            {
                RefCount--;
            }

            return RefCount;
        }

        public override string ToString()
        {
            return string.Format("{0} ref={1} type={2}", Key, RefCount, AssetType != null ? AssetType.Name : "-");
        }
    }
}
