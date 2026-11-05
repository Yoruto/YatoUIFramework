using System;
using UnityEngine;

namespace Yato.Resources
{
    /// <summary>Explicit Resources-only setup for a starter project. Production bootstraps may supply their own source.</summary>
    [DefaultExecutionOrder(-950)]
    public sealed class ResourcesServiceHost : MonoBehaviour, IResourceServiceSource
    {
        public IResourceService Resources { get; private set; }
        private void Awake() { Resources = ResourceServiceFactory.Create(new ResourcesProvider(), ownsProvider: true); }
        private void OnDestroy() { (Resources as IDisposable)?.Dispose(); Resources = null; }
    }
}
