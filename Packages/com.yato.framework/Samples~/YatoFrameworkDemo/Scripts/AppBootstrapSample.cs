using System;
using UnityEngine;
using Yato.App;
using Yato.Audio;
using Yato.Core;
using Yato.Persistence;
using Yato.Resources;
using Yato.Settings;

namespace Yato.FrameworkDemo
{
    /// <summary>
    /// 示例：全局服务在 Bootstrap 之前注册（D02）。
    /// <para>必须用 <see cref="RuntimeInitializeOnLoadMethodAttribute"/>（BeforeSceneLoad）注册：
    /// 编辑器自动初始化和 Bootstrap Scene 都会在场景加载后初始化，Awake/Start 里注册已经太晚。</para>
    /// <para>与 <c>GameStart</c> 二选一：两者都配置入口会在同一场景里竞争 UIRouter。</para>
    /// </summary>
    public static class AppBootstrapSample
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterServices()
        {
            GameBootstrap.RegisterService(new DemoAppService());
        }
    }

    /// <summary>示例服务：设置 → 音频音量联动，并订阅场景事件驱动 Loading 提示。</summary>
    public sealed class DemoAppService : IAppService
    {
        private AudioService m_audio;
        private SettingsService<DemoSettings> m_settings;

        public string Name
        {
            get { return "DemoAppService"; }
        }

        /// <summary>数值越大越晚初始化；SceneService 内部 Order 为 -100。</summary>
        public int Order
        {
            get { return 100; }
        }

        public void Initialize()
        {
            // 1) 设置：缺文件时使用默认值，损坏时保留原文件（D05）
            var store = new FileStore();
            m_settings = new SettingsService<DemoSettings>(new DemoSettings(), store, "settings/demo.json");
            SettingsLoadResult load = m_settings.Load();
            Debug.LogFormat("[Demo] 设置加载结果：{0}", load);

            // 2) 音频：音量来自设置，设置变化时同步到通道（D06）
            var resources = ResourceServiceFactory.Create();
            m_audio = new AudioService(resources, new AudioServiceOptions { SfxPoolCapacity = 8 });
            ApplyVolumes(m_settings.Current);
            m_settings.Changed += ApplyVolumes;

            // 3) 场景：订阅事件驱动 Loading UI（D03）。实际加载示例：
            //    IAwaitable<SceneLoadResult> run = GameBootstrap.Scene.LoadAsync(
            //        new SceneLoadRequest("Gameplay"));
            ISceneService scenes = GameBootstrap.Scene;
            if (scenes != null)
            {
                scenes.LoadStarted += delegate(SceneLoadRequest request)
                {
                    Debug.Log("[Demo] 开始场景操作：" + request.SceneName);
                };
                scenes.LoadCompleted += delegate(SceneLoadResult result)
                {
                    Debug.Log("[Demo] 场景操作结束：" + result);
                };
            }
        }

        public void Shutdown()
        {
            if (m_settings != null)
            {
                m_settings.Changed -= ApplyVolumes;
                m_settings = null;
            }

            if (m_audio != null)
            {
                m_audio.Dispose();
                m_audio = null;
            }
        }

        private void ApplyVolumes(DemoSettings settings)
        {
            if (m_audio == null)
            {
                return;
            }

            m_audio.SetBusVolume(AudioBus.Master, settings.MasterVolume);
            m_audio.SetBusVolume(AudioBus.Music, settings.MusicVolume);
            m_audio.SetBusVolume(AudioBus.Sfx, settings.SfxVolume);
        }
    }

    /// <summary>示例设置 DTO：游戏自定义的强类型字段（框架不规定业务字段）。</summary>
    [Serializable]
    public sealed class DemoSettings
    {
        public float MasterVolume = 1f;
        public float MusicVolume = 0.8f;
        public float SfxVolume = 1f;
    }
}
