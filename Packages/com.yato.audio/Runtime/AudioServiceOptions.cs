using UnityEngine;

namespace Yato.Audio
{
    /// <summary>SFX 池满时的拒绝策略（D06）。</summary>
    public enum SfxOverflowPolicy
    {
        /// <summary>默认：拒绝新请求（丢弃新的低优先级请求），不打断正在播放的音效。</summary>
        DropNewest = 0,

        /// <summary>新请求优先级高于池内最低优先级实例时抢占它，否则拒绝。</summary>
        ReplaceLowestPriority = 1
    }

    /// <summary>音频服务构造选项。</summary>
    public sealed class AudioServiceOptions
    {
        /// <summary>SFX AudioSource 池容量，默认 16。</summary>
        public int SfxPoolCapacity = 16;

        /// <summary>池满时的拒绝策略，默认丢弃新请求。</summary>
        public SfxOverflowPolicy OverflowPolicy = SfxOverflowPolicy.DropNewest;

        /// <summary>音乐淡入淡出/交叉渐变默认时长（秒）。</summary>
        public float DefaultFadeSeconds = 0.5f;

        /// <summary>可选音量映射（如 AudioMixer 暴露参数）；为空时只调整 AudioSource 音量。</summary>
        public IAudioVolumeSink VolumeSink;

        /// <summary>
        /// 可选挂载点。为空时服务会创建并托管 <c>[Yato.Audio]</c> DDOL 根节点；
        /// 提供时只在该节点下创建子 AudioSource，不销毁调用方的节点。
        /// </summary>
        public Transform Root;
    }
}
