using System;
using UnityEngine;

namespace Yato.Resources
{
    /// <summary>Optional persistent-cache integration. All callbacks run on the Unity main thread.</summary>
    public interface IBundleCachePolicy
    {
        string GetActiveDirectory(string persistentRoot);
        void MarkUsed(string directory);
        void PrepareStartup();
    }

    /// <summary>
    /// Bundle loading works without a cache extension. A HotUpdate or application-owned policy
    /// may be explicitly installed before creating bundle providers. Null restores the local-directory policy.
    /// </summary>
    public static class BundleCachePolicy
    {
        private static readonly IBundleCachePolicy Default = new LocalDirectoryPolicy();
        private static IBundleCachePolicy s_current = Default;
        internal static bool HasCustomPolicy { get { return !ReferenceEquals(s_current, Default); } }
        public static IBundleCachePolicy Current
        {
            get { return s_current; }
            set { s_current = value ?? Default; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_current = Default; }

        private sealed class LocalDirectoryPolicy : IBundleCachePolicy
        {
            public string GetActiveDirectory(string persistentRoot) { return persistentRoot; }
            public void MarkUsed(string directory) { }
            public void PrepareStartup() { }
        }
    }
}
