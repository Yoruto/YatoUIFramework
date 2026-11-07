using UnityEngine;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>
    /// Optional snapshot/cache integration for the Bundle package. Install explicitly in EditMode
    /// tools or tests; Player/Play Mode installs the default after all subsystem resets have run.
    /// Applications may replace BundleCachePolicy.Current before creating their providers.
    /// </summary>
    public sealed class HotUpdateBundleCachePolicy : IBundleCachePolicy
    {
        public static void Install() { BundleCachePolicy.Current = new HotUpdateBundleCachePolicy(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void InstallDefault()
        {
            if (!BundleCachePolicy.HasCustomPolicy) Install();
        }

        public string GetActiveDirectory(string persistentRoot)
        {
            return new HotUpdateStorage(persistentRoot).ActiveDirectory;
        }

        public void MarkUsed(string directory) { HotUpdateMaintenance.MarkUsed(directory); }

        public void PrepareStartup()
        {
            var result = HotUpdateMaintenance.PrepareStartup();
            if (result.Success)
                Log.Info(string.Format("[Yato.Resources] 启动缓存维护：删除 {0} 个旧快照（{1} 字节），保留 {2} 个",
                    result.DeletedDirectories, result.DeletedBytes, result.RetainedDirectories));
            else if (!result.Busy && !result.InUse)
                Log.Warn("[Yato.Resources] 启动缓存维护未完成（保留全部快照）：" + result.Error);
        }
    }
}
