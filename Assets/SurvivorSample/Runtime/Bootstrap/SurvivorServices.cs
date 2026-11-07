using System;
using UnityEngine;
using Yato.App;
using Yato.Audio;
using Yato.Config;
using Yato.Core;
using Yato.Input;
using Yato.Persistence;
using Yato.Resources;
using Yato.Save;
using Yato.Settings;
using Yato.Survivor.Domain;
using Yato.UI.Input;

namespace Yato.Survivor
{
    [Serializable] public sealed class SurvivorSettings
    {
        public float Master = .7f, Music = .35f, Effects = .65f;
        public string BindingOverrides;
    }

    public sealed class SurvivorServices : IAppService
    {
        private readonly string m_dataRoot;
        private readonly bool m_useBundles;
        public SurvivorServices(string dataRoot = null, bool useBundles = false)
        {
            m_dataRoot = string.IsNullOrWhiteSpace(dataRoot) ? null : dataRoot;
            m_useBundles = useBundles || !Application.isEditor;
        }
        public string Name => "Yato Survivor";
        public int Order => 100;
        public IResourceService Resources { get; private set; }
        public SettingsService<SurvivorSettings> Settings { get; private set; }
        public SaveService<SurvivorHistory> Saves { get; private set; }
        public IEventBus Events { get; } = new EventBus();
        public PauseService Pause { get; private set; }
        public AudioService Audio { get; private set; }
        public InputContextService Input { get; set; }
        public UIInputCoordinator UIInput { get; set; }
        public ConfigService Config { get; set; }
        public SurvivorSpriteSet Art { get; set; }
        public ResourceScope Scope { get; } = new ResourceScope("survivor/application");
        public void Initialize()
        {
            if (Resources != null) return;
            Resources = m_useBundles ? BundleResourceServiceFactory.Create() : ResourceServiceFactory.Create();
            var store = new FileStore(m_dataRoot);
            Settings = new SettingsService<SurvivorSettings>(new SurvivorSettings(), store, "survivor/settings.json", validator: value =>
            {
                if (float.IsNaN(value.Master) || float.IsNaN(value.Music) || float.IsNaN(value.Effects) ||
                    value.Master < 0 || value.Master > 1 || value.Music < 0 || value.Music > 1 || value.Effects < 0 || value.Effects > 1)
                    throw new ArgumentException("Invalid volume settings");
            });
            Settings.Load();
            Saves = new SaveService<SurvivorHistory>(store, 1, SurvivorHistory.Validate, directory: "survivor/history");
            Pause = new PauseService(pauseOnFocusLoss: true);
            Audio = new AudioService(Resources);
            Settings.Changed += ApplyAudio; ApplyAudio(Settings.Current);
        }
        private void ApplyAudio(SurvivorSettings settings)
        {
            Audio.SetBusVolume(AudioBus.Master, settings.Master);
            Audio.SetBusVolume(AudioBus.Music, settings.Music);
            Audio.SetBusVolume(AudioBus.Sfx, settings.Effects);
            Audio.SetBusVolume(AudioBus.Ui, settings.Effects);
        }
        public void Play(string id)
        {
            if (Art == null) return;
            var clip = id == "hit" ? Art.HitSound : id == "upgrade" ? Art.UpgradeSound : Art.PickupSound;
            if (clip != null) Audio.TryPlaySfx(new SfxRequest(clip) { Volume = .5f }, out _);
        }
        public void Shutdown()
        {
            UIInput?.Dispose(); UIInput = null;
            Input?.Dispose(); Input = null;
            if (Settings != null) Settings.Changed -= ApplyAudio;
            Audio?.Dispose(); Audio = null;
            Pause?.Dispose(); Pause = null;
            Events.Clear(); Scope.Dispose(); Config = null; Art = null;
            (Resources as IDisposable)?.Dispose(); Resources = null; Settings = null; Saves = null;
        }
    }
    public readonly struct SurvivorUiChanged
    {
        public readonly string RunId;
        public SurvivorUiChanged(string runId) { RunId = runId; }
    }
}
