namespace Yato.Audio
{
    /// <summary>音乐播放选项。</summary>
    public sealed class MusicOptions
    {
        /// <summary>默认选项（每次返回新实例，避免共享可变状态）。</summary>
        public static MusicOptions Default
        {
            get { return new MusicOptions(); }
        }

        /// <summary>淡入与交叉渐变时长（秒）。</summary>
        public float FadeSeconds = 0.5f;

        /// <summary>音乐音量（0..1），仍会乘以 Master × Music 通道音量。</summary>
        public float Volume = 1f;

        /// <summary>是否循环，默认 true。</summary>
        public bool Loop = true;

        /// <summary>同一首正在播放时是否重新开始，默认 false（继续播放）。</summary>
        public bool RestartIfSame;
    }
}
