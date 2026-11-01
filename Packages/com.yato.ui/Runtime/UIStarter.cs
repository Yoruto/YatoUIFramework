using System;
using UnityEngine;
using Yato.Resources;

namespace Yato.UI
{
    /// <summary>Minimal, explicit Resources-only UI setup for new projects. Custom bootstraps configure UIRouter directly.</summary>
    [DefaultExecutionOrder(-900)]
    public sealed class UIStarter : MonoBehaviour
    {
        public UIPageCatalog Catalog;
        [Tooltip("Component implementing IResourceServiceSource; the setup wizard supplies a ResourcesServiceHost.")]
        public MonoBehaviour ResourceSource;
        public string InitialPageId = "Main";
        private UIPageHandle m_page;
        private IResourceService m_resources;
        private bool m_disposed;
        private async void Start()
        {
            try
            {
                if (Catalog == null) throw new InvalidOperationException("UIStarter requires a page catalog");
                var definition = Catalog.Get(InitialPageId);
                m_resources = (ResourceSource as IResourceServiceSource)?.Resources;
                if (m_resources == null) throw new InvalidOperationException("UIStarter requires an initialized IResourceServiceSource");
                UIRouter.Configure(m_resources);
                var page = await UIRouter.OpenAsync(definition);
                if (m_disposed) { page.Close(); return; }
                m_page = page;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!m_disposed) Yato.Core.Log.Error("UIStarter", "Opening the initial page failed", error); }
        }
        private void OnDestroy()
        {
            m_disposed = true;
            UIRouter.CancelOpen(InitialPageId);
            m_page?.Close();
            m_resources = null;
        }
    }
}
