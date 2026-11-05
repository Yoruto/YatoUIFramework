using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yato.App;
using Yato.Resources;
using Yato.UI;

namespace Yato.BasicSample
{
    public sealed class BasicSampleEntry : MonoBehaviour
    {
        private readonly UIPageDef m_page = new UIPageDef("YatoBasicPage", null,
            new AssetKey("yato/basic/page"), cacheable: false);
        private IResourceService m_resources;
        private string m_status = "Ready";
        private void Awake()
        {
            GameBootstrap.Initialize();
            m_resources = ResourceServiceFactory.Create(new ResourcesProvider());
            UIRouter.Configure(m_resources); // No Facade, Mediator or Proxy required.
        }
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20, 20, 330, 300), GUI.skin.box);
            GUILayout.Label("Yato Basic Sample - " + SceneManager.GetActiveScene().name);
            GUILayout.Label(m_status);
            if (GUILayout.Button("Open page (no PureMVC)"))
                Observe(UIRouter.OpenAsync(m_page), value => "Opened " + value.PageId);
            if (GUILayout.Button("Close pages and release resources")) UIRouter.CloseAll();
            if (GUILayout.Button("Switch scene (Single)"))
            {
                string target = SceneManager.GetActiveScene().name == "YatoBasic" ? "YatoBasicSecond" : "YatoBasic";
                Observe(GameBootstrap.Scene.LoadAsync(new SceneLoadRequest(target)), value => value.ToString());
            }
            GUILayout.Label("Watch Console for acquire/release and scene lifecycle.");
            GUILayout.EndArea();
        }
        private void Observe<T>(IAwaitable<T> operation, Func<T, string> display)
        {
            operation.Completed += () =>
            {
                if (this == null) return;
                try { m_status = display(operation.GetResult()); }
                catch (Exception error) { m_status = error.Message; }
            };
        }
        private void OnDestroy()
        {
            UIRouter.CancelOpen(m_page.PageId);
            UIRouter.CloseAll();
            (m_resources as IDisposable)?.Dispose();
            m_resources = null;
        }
    }
}
