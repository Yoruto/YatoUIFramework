using UnityEngine;
using Yato.Resources;

namespace Yato.Audio
{
    /// <summary>
    /// 一次音效播放请求（D06）：2D/3D、一次性/循环、优先级与音量。
    /// <para>提供 <see cref="Clip"/> 或 <see cref="ClipKey"/> 之一；使用 key 时由服务经资源层加载并持有句柄。</para>
    /// </summary>
    public sealed class SfxRequest
    {
        public SfxRequest(AssetKey clipKey)
        {
            ClipKey = clipKey;
        }

        public SfxRequest(AudioClip clip)
        {
            Clip = clip;
        }

        /// <summary>直接提供的音频资源（不归服务持有）。</summary>
        public AudioClip Clip;

        /// <summary>资源 key；<see cref="Clip"/> 为空时使用。</summary>
        public AssetKey ClipKey;

        /// <summary>播放通道：只能使用 Sfx / Ui / Voice。</summary>
        public AudioBus Bus = AudioBus.Sfx;

        /// <summary>请求音量（0..1），仍会乘以 Master × 通道音量。</summary>
        public float Volume = 1f;

        /// <summary>音高，默认 1。</summary>
        public float Pitch = 1f;

        /// <summary>是否循环；循环音效必须由持有者显式 Stop。</summary>
        public bool Loop;

        /// <summary>优先级，数值越大越重要；用于池满时的抢占判定。</summary>
        public int Priority;

        /// <summary>是否使用 3D 空间衰减。</summary>
        public bool Spatial;

        /// <summary>3D 音效的世界坐标；2D 时忽略。</summary>
        public Vector3 Position;

        /// <summary>空间混合（0=2D，1=3D）；Spatial=true 且为 0 时按 1 处理。</summary>
        public float SpatialBlend;

        public float MinDistance = 1f;

        public float MaxDistance = 50f;

        public AudioRolloffMode Rolloff = AudioRolloffMode.Logarithmic;

        public bool IsValid
        {
            get { return Clip != null || ClipKey.IsValid; }
        }

        public override string ToString()
        {
            return Clip != null
                ? string.Format("Sfx({0}, bus={1}, loop={2})", Clip.name, Bus, Loop)
                : string.Format("Sfx({0}, bus={1}, loop={2})", ClipKey, Bus, Loop);
        }
    }
}
