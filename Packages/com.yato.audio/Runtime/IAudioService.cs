using System;
using System.Threading;
using UnityEngine;
using Yato.Resources;

namespace Yato.Audio
{
    /// <summary>
    /// 音频服务（D06）：通道音量、双源音乐交叉渐变、SFX 池与句柄生命周期。
    /// <para>跨场景音乐归属于全局生命周期（服务托管 DDOL 根节点）；场景音效由持有者把 <see cref="ISfxHandle"/> 绑定到场景资源范围。</para>
    /// </summary>
    public interface IAudioService : IDisposable
    {
        int SfxPoolCapacity { get; }

        /// <summary>当前占用中的音效实例数（含加载中）。</summary>
        int ActiveSfxCount { get; }

        AudioClip CurrentMusic { get; }

        bool IsMusicPlaying { get; }

        bool IsMusicPaused { get; }

        /// <summary>当前音乐 AudioSource 的实际音量（含淡入淡出与通道音量，诊断用）。</summary>
        float CurrentMusicVolume { get; }

        float MasterVolume { get; }

        /// <summary>通道自身音量（不含 Master）。</summary>
        float GetBusVolume(AudioBus bus);

        /// <summary>实际音量 = Master × 通道音量。</summary>
        float GetEffectiveVolume(AudioBus bus);

        void SetBusVolume(AudioBus bus, float volume);

        event Action<AudioBus, float> BusVolumeChanged;

        event Action<AudioClip> MusicChanged;

        /// <summary>播放/交叉渐变到指定音乐（立即开始淡入）。</summary>
        void PlayMusic(AudioClip clip, MusicOptions options = null);

        /// <summary>经资源层加载音乐并播放；加载失败时 awaitable 抛异常。</summary>
        IAwaitable<AudioClip> PlayMusicAsync(
            AssetKey clipKey,
            MusicOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken));

        void PauseMusic();

        void ResumeMusic();

        /// <summary>停止音乐；<paramref name="fadeOutSeconds"/> &lt; 0 时使用默认淡出时长。</summary>
        void StopMusic(float fadeOutSeconds = -1f);

        /// <summary>
        /// 尝试播放音效。池满且策略拒绝时返回 false 且不产生句柄；
        /// 接受时即使资源仍在加载也会立即返回句柄。
        /// </summary>
        bool TryPlaySfx(SfxRequest request, out ISfxHandle handle);

        /// <summary>停止全部音乐与音效、释放资源句柄；可重复调用。</summary>
        void Shutdown();
    }
}
