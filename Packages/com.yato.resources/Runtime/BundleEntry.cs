namespace Yato.Resources
{
    /// <summary>
    /// bundle 运行时记录（文档 3.3 的 BundleEntry）。
    /// 引用计数由 BundleProvider 维护，归零后进入延迟卸载队列，禁止 Unload(true)。
    /// </summary>
    public sealed class BundleEntry
    {
        public string Name;

        /// <summary>引用计数：被资产占用或被其它 bundle 依赖时 &gt; 0。</summary>
        public int RefCount;

        public long SizeBytes;

        public float LoadSeconds;

        public bool IsLoaded;

        /// <summary>是否已进入延迟卸载队列，防止重复入队（RES-02）。</summary>
        public bool IsPendingUnload;

        public int AddRef()
        {
            IsPendingUnload = false;
            return ++RefCount;
        }

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
            return string.Format("{0} ref={1} loaded={2} size={3}", Name, RefCount, IsLoaded, SizeBytes);
        }
    }
}
