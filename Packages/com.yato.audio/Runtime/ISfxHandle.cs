using System;

namespace Yato.Audio
{
    /// <summary>
    /// 一次音效播放的句柄（D06）：音效结束、显式停止或被 Disposing 后回池。
    /// <para>实现了 <see cref="IDisposable"/>，可直接用场景资源范围 <c>ResourceScope.Track(handle)</c> 绑定生命周期。</para>
    /// </summary>
    public interface ISfxHandle : IDisposable
    {
        AudioBus Bus { get; }

        int Priority { get; }

        /// <summary>是否正在播放（加载中为 false）。</summary>
        bool IsPlaying { get; }

        /// <summary>是否已结束/停止并归还池位。</summary>
        bool IsReleased { get; }

        /// <summary>请求音量（0..1）。</summary>
        float Volume { get; set; }

        /// <summary>停止播放；<paramref name="fadeOutSeconds"/> &gt; 0 时淡出后回池。</summary>
        void Stop(float fadeOutSeconds = 0f);
    }
}
