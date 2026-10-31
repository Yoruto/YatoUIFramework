using System;
using System.Threading;
using UnityEngine;
using Yato.App;
using Yato.Audio;
using Yato.Persistence;
using Yato.Resources;
using Yato.Settings;
using Yato.UI;

namespace Yato.FullSample
{
    public sealed class FullSampleEntry : MonoBehaviour
    {
        private FullSampleService m_service;
        private CancellationTokenSource m_update;
        private string m_url = "https://example.com/bundles";
        private string m_status = "Ready";
        private bool m_localTest;
        private readonly UIPageDef m_page = new UIPageDef("YatoFullPage", null, new AssetKey("yato/full/page"));
        private void Awake()
        {
            m_service = new FullSampleService();
            GameBootstrap.RegisterService(m_service);
            GameBootstrap.Initialize();
            UIRouter.Configure(m_service.Resources);
            if (FindObjectOfType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
        }
        private void OnGUI()
        {
            if (m_service == null || m_service.Resources == null) return;
            GUILayout.BeginArea(new Rect(20, 20, 340, 450), GUI.skin.box);
            GUILayout.Label("Yato Full Sample");
            GUILayout.Label(m_status);
            if (GUILayout.Button("Open UI page")) Observe(UIRouter.OpenAsync(m_page), x => x.PageId);
            if (GUILayout.Button("Play generated tone")) m_service.PlayTone();
            GUILayout.Label("Master volume (Apply only)");
            float volume = GUILayout.HorizontalSlider(m_service.Volume, 0f, 1f);
            if (Math.Abs(volume - m_service.Volume) > 0.001f) m_service.ApplyVolume(volume);
            if (GUILayout.Button("Save settings explicitly")) { m_service.Save(); m_status = "Settings saved"; }
            GUILayout.Label("Hot update root URL (published ABs required)");
            m_url = GUILayout.TextField(m_url);
            m_localTest = GUILayout.Toggle(m_localTest, "Allow local file:// / loopback HTTP testing");
            if (m_update == null && GUILayout.Button("Run update"))
            {
                m_update = new CancellationTokenSource();
                var downloader = new RemoteDownloader { AllowFileUrls = m_localTest, AllowInsecureLocalhost = m_localTest };
                var operation = new HotUpdateService(downloader).RunAsync(m_url, ct: m_update.Token);
                Observe(operation, x => x.ToString());
                operation.Completed += () => { m_update?.Dispose(); m_update = null; };
            }
            if (m_update != null && GUILayout.Button("Cancel update before commit")) m_update.Cancel();
            GUILayout.Label("Input is optional; import its package separately.");
            GUILayout.EndArea();
        }
        private void Observe<T>(IAwaitable<T> operation, Func<T, string> text)
        {
            operation.Completed += () =>
            {
                if (this == null) return;
                try { m_status = text(operation.GetResult()); }
                catch (Exception e) { m_status = e.Message; }
            };
        }
        private void OnDestroy()
        {
            m_update?.Cancel();
            m_update?.Dispose();
            m_update = null;
            UIRouter.CancelOpen(m_page.PageId);
            UIRouter.CloseAll();
            GameBootstrap.Shutdown();
        }
    }

    [Serializable] public sealed class FullSampleSettings { public float Volume = 0.5f; }

    public sealed class FullSampleService : IAppService
    {
        private SettingsService<FullSampleSettings> m_settings;
        private AudioService m_audio;
        private AudioClip m_tone;
        private ISceneService m_scenes;
        public IResourceService Resources { get; private set; }
        public string Name => "FullSample";
        public int Order => 100;
        public float Volume => m_settings.Current.Volume;
        public void Initialize()
        {
            if (m_settings != null) return;
            try
            {
                Resources = ResourceServiceFactory.Create(new ResourcesProvider());
                m_settings = new SettingsService<FullSampleSettings>(new FullSampleSettings(), new FileStore(), "settings/yato-full-sample.json");
                Debug.Log("[FullSample] Settings: " + m_settings.Load());
                m_audio = new AudioService(Resources);
                m_settings.Changed += ApplyAudio;
                ApplyAudio(m_settings.Current);
                m_scenes = GameBootstrap.Scene;
                m_scenes.LoadStarted += OnLoadStarted;
                m_scenes.LoadCompleted += OnLoadCompleted;
                m_tone = AudioClip.Create("Sample tone", 22050 / 4, 1, 22050, false);
                var samples = new float[m_tone.samples];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = 0.1f * Mathf.Sin(2 * Mathf.PI * 440 * i / 22050f);
                m_tone.SetData(samples, 0);
            }
            catch { Shutdown(); throw; }
        }
        public void PlayTone() { m_audio.TryPlaySfx(new SfxRequest(m_tone), out _); }
        public void ApplyVolume(float value) { m_settings.Apply(settings => settings.Volume = value); }
        public void Save() { m_settings.Save(); }
        private void ApplyAudio(FullSampleSettings settings) { m_audio.SetBusVolume(AudioBus.Master, settings.Volume); }
        private void OnLoadStarted(SceneLoadRequest request) { Debug.Log("[FullSample] Loading " + request.SceneName); }
        private void OnLoadCompleted(SceneLoadResult result) { Debug.Log("[FullSample] Loaded " + result); }
        public void Shutdown()
        {
            if (m_scenes != null) { m_scenes.LoadStarted -= OnLoadStarted; m_scenes.LoadCompleted -= OnLoadCompleted; m_scenes = null; }
            if (m_settings != null) { m_settings.Changed -= ApplyAudio; m_settings = null; }
            m_audio?.Dispose(); m_audio = null;
            if (m_tone != null) UnityEngine.Object.Destroy(m_tone);
            m_tone = null;
            Resources?.ReleaseUnused(); Resources = null;
        }
    }
}
