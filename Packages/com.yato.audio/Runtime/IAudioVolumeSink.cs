using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Yato.Audio
{
    /// <summary>
    /// 音量映射出口（D06）：用于把线性音量同步到 AudioMixer 暴露参数等外部系统。
    /// Audio 不负责把设置落盘；由游戏把 Settings 的变化转成 <see cref="IAudioService.SetBusVolume"/> 调用。
    /// </summary>
    public interface IAudioVolumeSink
    {
        void Apply(AudioBus bus, float linearVolume);
    }

    /// <summary>
    /// 把通道音量写入 AudioMixer 参数（线性 → 分贝）。参数名可逐个覆盖；
    /// 未配置参数的通道会被忽略。
    /// </summary>
    public sealed class AudioMixerVolumeSink : IAudioVolumeSink
    {
        private const float MinDecibels = -80f;

        private readonly AudioMixer m_mixer;
        private readonly Dictionary<AudioBus, string> m_parameters = new Dictionary<AudioBus, string>();

        public AudioMixerVolumeSink(AudioMixer mixer)
        {
            m_mixer = mixer;

            m_parameters[AudioBus.Master] = "MasterVolume";
            m_parameters[AudioBus.Music] = "MusicVolume";
            m_parameters[AudioBus.Sfx] = "SfxVolume";
            m_parameters[AudioBus.Ui] = "UiVolume";
            m_parameters[AudioBus.Voice] = "VoiceVolume";
        }

        /// <summary>覆盖某通道的参数名；传空串表示该通道不写入 Mixer。</summary>
        public void SetParameter(AudioBus bus, string parameterName)
        {
            m_parameters[bus] = parameterName;
        }

        public void Apply(AudioBus bus, float linearVolume)
        {
            if (m_mixer == null)
            {
                return;
            }

            string parameter;
            if (!m_parameters.TryGetValue(bus, out parameter) || string.IsNullOrEmpty(parameter))
            {
                return;
            }

            try
            {
                m_mixer.SetFloat(parameter, linearVolume <= 0.0001f
                    ? MinDecibels
                    : Mathf.Log10(Mathf.Clamp01(linearVolume)) * 20f);
            }
            catch (Exception)
            {
                // Mixer 参数缺失不应影响游戏运行；由接入方在编辑器里核对参数名
            }
        }
    }
}
