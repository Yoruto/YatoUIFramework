using System;
using UnityEngine;

namespace Yato.Resources
{
    /// <summary>Resource service ownership and diagnostics. Bundle initialization is provided by the optional Bundle package.</summary>
    public static class ResourceServiceFactory
    {
        public static IResourceService Current { get; private set; }

        internal static void Forget(IResourceService service)
        {
            if (ReferenceEquals(Current, service)) Current = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            (Current as IDisposable)?.Dispose();
            Current = null;
        }

        /// <summary>Default providers are service-owned. Injected providers remain caller-owned unless ownership is explicitly transferred.</summary>
        public static IResourceService Create(IResourceProvider provider = null) => Create(provider, false);

        public static IResourceService Create(IResourceProvider provider, bool ownsProvider)
        {
            var service = new ResourceService(provider ?? CreateDefaultProvider(), provider == null || ownsProvider);
            Current = service;
            return service;
        }

        private static IResourceProvider CreateDefaultProvider()
        {
#if UNITY_EDITOR
            return new EditorAssetProvider();
#else
            return new ResourcesProvider();
#endif
        }
    }
}
