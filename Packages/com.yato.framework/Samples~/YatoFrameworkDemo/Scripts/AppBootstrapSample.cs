using System;
using UnityEngine;
using Yato.App;
using Yato.Audio;
using Yato.Persistence;
using Yato.Resources;
using Yato.Settings;

namespace Yato.FrameworkDemo
{
    /// <summary>Legacy opt-in example. Call RegisterServices before GameBootstrap.Initialize.</summary>
    public static class AppBootstrapSample
    {
        public static void RegisterServices() { GameBootstrap.RegisterService(new DemoAppService()); }
    }

    public sealed class DemoAppService : IAppService
    {
        private AudioService m_audio;
        private IResourceService m_resources;
        private SettingsService<DemoSettings> m_settings;
        private ISceneService m_scenes;
        private Action<SceneLoadRequest> m_loadStarted;
        private Action<SceneLoadResult> m_loadCompleted;
        private bool m_initialized;
        public string Name => "DemoAppService";
        public int Order => 100;

        public void Initialize()
        {
            if (m_initialized) return;
            try
            {
                m_settings = new SettingsService<DemoSettings>(new DemoSettings(), new FileStore(), "settings/demo.json");
                Debug.Log("[Demo] Settings: " + m_settings.Load());
                m_resources = ResourceServiceFactory.Create(new ResourcesProvider());
                m_audio = new AudioService(m_resources, new AudioServiceOptions { SfxPoolCapacity = 8 });
                ApplyVolumes(m_settings.Current);
                m_settings.Changed += ApplyVolumes;
                m_scenes = GameBootstrap.Scene;
                m_loadStarted = request => Debug.Log("[Demo] Loading " + request.SceneName);
                m_loadCompleted = result => Debug.Log("[Demo] Loaded " + result);
                if (m_scenes != null)
                {
                    m_scenes.LoadStarted += m_loadStarted;
                    m_scenes.LoadCompleted += m_loadCompleted;
                }
                m_initialized = true;
            }
            catch { Shutdown(); throw; }
        }

        public void Shutdown()
        {
            m_initialized = false;
            if (m_scenes != null)
            {
                m_scenes.LoadStarted -= m_loadStarted;
                m_scenes.LoadCompleted -= m_loadCompleted;
                m_scenes = null;
            }
            m_loadStarted = null;
            m_loadCompleted = null;
            if (m_settings != null) { m_settings.Changed -= ApplyVolumes; m_settings = null; }
            m_audio?.Dispose();
            m_audio = null;
            m_resources?.ReleaseUnused();
            m_resources = null;
        }

        private void ApplyVolumes(DemoSettings settings)
        {
            if (m_audio == null) return;
            m_audio.SetBusVolume(AudioBus.Master, settings.MasterVolume);
            m_audio.SetBusVolume(AudioBus.Music, settings.MusicVolume);
            m_audio.SetBusVolume(AudioBus.Sfx, settings.SfxVolume);
        }
    }

    [Serializable]
    public sealed class DemoSettings
    {
        public float MasterVolume = 1f;
        public float MusicVolume = 0.8f;
        public float SfxVolume = 1f;
    }
}
