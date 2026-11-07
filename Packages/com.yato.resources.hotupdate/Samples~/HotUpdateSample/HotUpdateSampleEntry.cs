using System;
using System.Threading;
using UnityEngine;
using Yato.Resources;

namespace Yato.HotUpdateSample
{
    /// <summary>Optional resource-update demo. Import only in projects that install the hot-update package.</summary>
    public sealed class HotUpdateSampleEntry : MonoBehaviour
    {
        private CancellationTokenSource m_update;
        private string m_url = "https://example.com/bundles";
        private string m_status = "Ready";
        private bool m_localTest;
        private bool m_destroyed;

        private void Awake()
        {
            HotUpdateBundleCachePolicy.Install();
            m_status = "Startup cache: " + HotUpdateMaintenance.PrepareStartup();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(400, 20, 400, 260), GUI.skin.box);
            GUILayout.Label("Optional resource hot update");
            GUILayout.Label(m_status);
            m_url = GUILayout.TextField(m_url);
            m_localTest = GUILayout.Toggle(m_localTest, "Allow local file:// / loopback HTTP testing");
            if (m_update == null && GUILayout.Button("Run update")) StartUpdate();
            if (m_update != null && GUILayout.Button("Cancel update before commit")) m_update.Cancel();
            GUILayout.EndArea();
        }

        private void StartUpdate()
        {
            var cancellation = new CancellationTokenSource();
            m_update = cancellation;
            var downloader = new RemoteDownloader { AllowFileUrls = m_localTest, AllowInsecureLocalhost = m_localTest };
            var operation = new HotUpdateService(downloader).RunAsync(m_url, ct: cancellation.Token);
            operation.Completed += () =>
            {
                try { if (!m_destroyed) m_status = operation.GetResult().ToString(); }
                catch (Exception error) { if (!m_destroyed) m_status = error.Message; }
                finally
                {
                    if (ReferenceEquals(m_update, cancellation)) m_update = null;
                    cancellation.Dispose();
                }
            };
        }

        private void OnDestroy()
        {
            m_destroyed = true;
            var cancellation = m_update;
            m_update = null;
            cancellation?.Cancel();
            cancellation?.Dispose();
        }
    }
}
