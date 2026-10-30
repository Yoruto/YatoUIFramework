using System.Collections.Generic;

namespace Yato.Resources
{
    /// <summary>运行时资源统计快照（诊断窗口 / 泄漏报告使用）。</summary>
    public sealed class ResourceStats
    {
        public static readonly ResourceStats Empty = new ResourceStats();

        /// <summary>当前常驻 bundle 数。</summary>
        public int LoadedBundleCount { get; set; }

        /// <summary>当前被句柄持有的资产数。</summary>
        public int ActiveAssetCount { get; set; }

        /// <summary>所有已加载 bundle 的总字节数。</summary>
        public long TotalBundleBytes { get; set; }

        /// <summary>常驻 bundle 明细。</summary>
        public IList<BundleStat> Bundles { get; set; }

        public ResourceStats()
        {
            Bundles = new List<BundleStat>(0);
        }

        public sealed class BundleStat
        {
            public string Name { get; set; }

            public int RefCount { get; set; }

            public long SizeBytes { get; set; }

            public float LoadSeconds { get; set; }

            public bool IsLoaded { get; set; }

            public override string ToString()
            {
                return string.Format("{0} ref={1} size={2} load={3:F1}ms", Name, RefCount, SizeBytes, LoadSeconds * 1000f);
            }
        }
    }
}
