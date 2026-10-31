using System;
using System.Threading;
using UnityEngine;
using Yato.Core;
using Yato.Resources;

namespace Yato.Audio
{
    /// <summary>
    /// 音频服务默认实现（D06）。
    /// <para>Music 使用两个长期 AudioSource 做淡入淡出与交叉渐变；SFX 使用固定容量池，满时按策略拒绝。</para>
    /// <para>经资源层加载的片段由句柄持有，音效结束/停止后归还；迟到的加载结果会被立即释放。</para>
    /// <para>不直接读写设置：音量变化由游戏把 Settings 转成 <see cref="SetBusVolume"/> 调用。</para>
    /// </summary>
    public sealed class AudioService : IAudioService
    {
        private const string RootObjectName = "[Yato.Audio]";

        private readonly IResourceService m_resources;
        private readonly AudioServiceOptions m_options;
        private readonly float[] m_busVolumes = new float[5];
        private readonly MusicSlot[] m_musicSlots = new MusicSlot[2];
        private readonly SfxSlot[] m_sfxSlots;

        private GameObject m_rootObject;
        private Transform m_root;
        private bool m_ownsRoot;
        private bool m_infrastructureReady;
        private bool m_disposed;

        private MusicSlot m_activeMusic;
        private bool m_musicPaused;
        private int m_musicRequestId;

        public AudioService(IResourceService resources, AudioServiceOptions options = null)
        {
            m_resources = resources ?? NullResourceService.Instance;
            m_options = options ?? new AudioServiceOptions();

            int capacity = m_options.SfxPoolCapacity > 0 ? m_options.SfxPoolCapacity : 1;
            m_sfxSlots = new SfxSlot[capacity];

            for (int i = 0; i < m_busVolumes.Length; i++)
            {
                m_busVolumes[i] = 1f;
            }

            EnsureInfrastructure();
            RuntimeHost.Updated += Tick;
        }

        public int SfxPoolCapacity
        {
            get { return m_sfxSlots.Length; }
        }

        public int ActiveSfxCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < m_sfxSlots.Length; i++)
                {
                    if (m_sfxSlots[i] != null && m_sfxSlots[i].Handle != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public AudioClip CurrentMusic
        {
            get
            {
                if (m_activeMusic == null || !m_activeMusic.InUse || m_activeMusic.Source == null)
                {
                    return null;
                }

                return m_activeMusic.Source.clip;
            }
        }

        public bool IsMusicPlaying
        {
            get
            {
                return m_activeMusic != null && m_activeMusic.InUse &&
                       m_activeMusic.Source != null && m_activeMusic.Source.isPlaying;
            }
        }

        public bool IsMusicPaused
        {
            get { return m_musicPaused; }
        }

        public float CurrentMusicVolume
        {
            get
            {
                return m_activeMusic != null && m_activeMusic.Source != null
                    ? m_activeMusic.Source.volume
                    : 0f;
            }
        }

        public float MasterVolume
        {
            get { return m_busVolumes[(int)AudioBus.Master]; }
        }

        public event Action<AudioBus, float> BusVolumeChanged;

        public event Action<AudioClip> MusicChanged;

        public float GetBusVolume(AudioBus bus)
        {
            int index = CheckBus(bus);
            return m_busVolumes[index];
        }

        public float GetEffectiveVolume(AudioBus bus)
        {
            int index = CheckBus(bus);
            float master = m_busVolumes[(int)AudioBus.Master];
            return bus == AudioBus.Master ? master : master * m_busVolumes[index];
        }

        public void SetBusVolume(AudioBus bus, float volume)
        {
            int index = CheckBus(bus);
            float clamped = Mathf.Clamp01(volume);
            if (Mathf.Approximately(m_busVolumes[index], clamped))
            {
                return;
            }

            m_busVolumes[index] = clamped;
            ApplyAllVolumes();

            if (m_options.VolumeSink != null)
            {
                try
                {
                    m_options.VolumeSink.Apply(bus, GetEffectiveVolume(bus));
                }
                catch (Exception e)
                {
                    Log.Error("AudioService", "音量映射失败：" + bus, e);
                }
            }

            if (BusVolumeChanged != null)
            {
                try
                {
                    BusVolumeChanged(bus, clamped);
                }
                catch (Exception e)
                {
                    Log.Error("AudioService", "BusVolumeChanged 回调异常", e);
                }
            }
        }

        public void PlayMusic(AudioClip clip, MusicOptions options = null)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException("AudioService");
            }

            // 直接播放同样是「最新的播放意图」：进行中的异步请求必须作废，不能反过来覆盖它
            m_musicRequestId++;
            PlayMusicCore(clip, null, options);
        }

        public IAwaitable<AudioClip> PlayMusicAsync(
            AssetKey clipKey,
            MusicOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var source = new Yato.Resources.AwaitableCompletionSource<AudioClip>();

            if (!clipKey.IsValid)
            {
                source.SetException(new ArgumentException("音乐资源 key 非法", "clipKey"));
                return source;
            }

            if (m_disposed)
            {
                source.SetException(new ObjectDisposedException("AudioService"));
                return source;
            }

            int requestId = ++m_musicRequestId;

            IAwaitable<IAssetHandle<AudioClip>> loading;
            try
            {
                loading = m_resources.LoadAsync<AudioClip>(clipKey, cancellationToken);
            }
            catch (Exception e)
            {
                source.SetException(e);
                return source;
            }

            loading.Completed += delegate
            {
                IAssetHandle<AudioClip> handle = null;
                try
                {
                    handle = loading.GetResult();

                    // 迟到的结果：被更新的请求取代或服务已关闭，立即归还资源
                    if (m_disposed || requestId != m_musicRequestId)
                    {
                        handle.Dispose();
                        source.SetException(new OperationCanceledException("音乐请求已被更新的请求取代"));
                        return;
                    }

                    // 先取出资源引用：同一首继续播放时 PlayMusicCore 会归还多余句柄，释放后 Asset 即为 null
                    AudioClip clip = handle.Asset;
                    PlayMusicCore(clip, handle, options);
                    source.SetResult(clip);
                }
                catch (Exception e)
                {
                    if (handle != null)
                    {
                        handle.Dispose();
                    }

                    source.SetException(e);
                }
            };

            return source;
        }

        public void PauseMusic()
        {
            if (m_disposed || m_activeMusic == null || !m_activeMusic.InUse || m_activeMusic.Source == null)
            {
                return;
            }

            m_activeMusic.Source.Pause();
            m_musicPaused = true;
        }

        public void ResumeMusic()
        {
            if (m_disposed || m_activeMusic == null || !m_activeMusic.InUse || m_activeMusic.Source == null)
            {
                return;
            }

            m_activeMusic.Source.UnPause();
            m_musicPaused = false;
        }

        public void StopMusic(float fadeOutSeconds = -1f)
        {
            if (m_disposed)
            {
                return;
            }

            // 停止是最新的播放意图：作废进行中的异步请求，避免它稍后把音乐重新播起来
            m_musicRequestId++;

            float fade = fadeOutSeconds < 0f ? m_options.DefaultFadeSeconds : fadeOutSeconds;
            MusicSlot slot = m_activeMusic;

            m_activeMusic = null;
            m_musicPaused = false;

            if (slot != null)
            {
                if (fade <= 0f)
                {
                    StopSlot(slot);
                }
                else
                {
                    slot.FadeTarget = 0f;
                    slot.FadeSpeed = 1f / fade;
                    slot.Stopping = true;
                }
            }

            RaiseMusicChanged(null);
        }

        public bool TryPlaySfx(SfxRequest request, out ISfxHandle handle)
        {
            handle = null;

            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            if (!request.IsValid)
            {
                throw new ArgumentException("SfxRequest 必须提供 Clip 或有效的 ClipKey", "request");
            }

            if (request.Bus == AudioBus.Master || request.Bus == AudioBus.Music)
            {
                throw new ArgumentException("音效只能使用 Sfx / Ui / Voice 通道", "request");
            }

            if (m_disposed)
            {
                return false;
            }

            EnsureInfrastructure();

            SfxSlot slot = AcquireSfxSlot(request);
            if (slot == null)
            {
                return false;
            }

            var sfx = new SfxHandle(this, slot, request);
            slot.Handle = sfx;
            handle = sfx;

            if (request.Clip != null)
            {
                sfx.Start(request.Clip);
            }
            else
            {
                sfx.BeginLoad();
            }

            return true;
        }

        public void Shutdown()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            RuntimeHost.Updated -= Tick;

            // 让进行中的音乐请求作废
            m_musicRequestId++;

            for (int i = 0; i < m_musicSlots.Length; i++)
            {
                if (m_musicSlots[i] != null)
                {
                    StopSlot(m_musicSlots[i]);
                }
            }

            m_activeMusic = null;
            m_musicPaused = false;

            for (int i = 0; i < m_sfxSlots.Length; i++)
            {
                SfxHandle sfx = m_sfxSlots[i] != null ? m_sfxSlots[i].Handle : null;
                if (sfx != null)
                {
                    sfx.StopImmediate();
                }
            }

            if (m_ownsRoot && m_rootObject != null)
            {
                UnityEngine.Object.Destroy(m_rootObject);
            }

            m_rootObject = null;
            m_root = null;
            m_infrastructureReady = false;
        }

        public void Dispose()
        {
            Shutdown();
        }

        private void PlayMusicCore(AudioClip clip, IAssetHandle<AudioClip> handle, MusicOptions options)
        {
            if (clip == null)
            {
                if (handle != null)
                {
                    handle.Dispose();
                }

                throw new ArgumentNullException("clip");
            }

            EnsureInfrastructure();
            options = options ?? MusicOptions.Default;

            if (m_activeMusic != null && m_activeMusic.InUse &&
                m_activeMusic.Source != null && m_activeMusic.Source.clip == clip &&
                !options.RestartIfSame)
            {
                // 同一首仍在播放：多余的加载结果直接归还
                if (handle != null)
                {
                    handle.Dispose();
                }

                return;
            }

            MusicSlot incoming = GetSecondaryMusicSlot();
            if (incoming.InUse)
            {
                StopSlot(incoming);
            }

            MusicSlot outgoing = m_activeMusic;
            if (ReferenceEquals(outgoing, incoming))
            {
                outgoing = null;
            }

            incoming.InUse = true;
            incoming.Source.clip = clip;
            incoming.Source.loop = options.Loop;
            incoming.Volume = Mathf.Clamp01(options.Volume);
            incoming.Fade = 0f;
            incoming.FadeTarget = 1f;
            incoming.FadeSpeed = 1f / Mathf.Max(0.0001f, options.FadeSeconds);
            incoming.Stopping = false;
            incoming.HasPlayed = false;
            incoming.Handle = handle;

            ApplyMusicVolume(incoming);
            incoming.Source.Play();

            m_activeMusic = incoming;
            m_musicPaused = false;

            if (outgoing != null && outgoing.InUse)
            {
                outgoing.FadeTarget = 0f;
                outgoing.FadeSpeed = 1f / Mathf.Max(0.0001f, options.FadeSeconds);
                outgoing.Stopping = true;
            }

            RaiseMusicChanged(clip);
        }

        private void Tick(float deltaTime)
        {
            if (m_disposed)
            {
                return;
            }

            UpdateMusic(deltaTime);
            UpdateSfx(deltaTime);
        }

        private void UpdateMusic(float deltaTime)
        {
            for (int i = 0; i < m_musicSlots.Length; i++)
            {
                MusicSlot slot = m_musicSlots[i];
                if (slot == null || !slot.InUse)
                {
                    continue;
                }

                if (slot.Source == null)
                {
                    StopSlot(slot);
                    continue;
                }

                if (!Mathf.Approximately(slot.Fade, slot.FadeTarget))
                {
                    slot.Fade = Mathf.MoveTowards(
                        slot.Fade, slot.FadeTarget, Mathf.Max(0.0001f, slot.FadeSpeed) * deltaTime);
                    ApplyMusicVolume(slot);
                }

                if (slot.Stopping && slot.Fade <= 0.0001f)
                {
                    StopSlot(slot);
                    continue;
                }

                if (slot.FadeTarget > 0f && !slot.Source.loop && !m_musicPaused)
                {
                    slot.HasPlayed |= slot.Source.isPlaying;
                    if (slot.HasPlayed && !slot.Source.isPlaying)
                    {
                        bool wasActive = ReferenceEquals(m_activeMusic, slot);
                        StopSlot(slot);
                        if (wasActive)
                        {
                            RaiseMusicChanged(null);
                        }
                    }
                }
            }
        }

        private void UpdateSfx(float deltaTime)
        {
            for (int i = 0; i < m_sfxSlots.Length; i++)
            {
                SfxHandle handle = m_sfxSlots[i] != null ? m_sfxSlots[i].Handle : null;
                if (handle != null)
                {
                    handle.Update(deltaTime);
                }
            }
        }

        private void ApplyAllVolumes()
        {
            for (int i = 0; i < m_musicSlots.Length; i++)
            {
                MusicSlot slot = m_musicSlots[i];
                if (slot != null && slot.InUse)
                {
                    ApplyMusicVolume(slot);
                }
            }

            for (int i = 0; i < m_sfxSlots.Length; i++)
            {
                SfxHandle handle = m_sfxSlots[i] != null ? m_sfxSlots[i].Handle : null;
                if (handle != null)
                {
                    handle.ReapplyVolume();
                }
            }
        }

        private void ApplyMusicVolume(MusicSlot slot)
        {
            if (slot.Source == null)
            {
                return;
            }

            slot.Source.volume = slot.Volume * GetEffectiveVolume(AudioBus.Music) * slot.Fade;
        }

        private void StopSlot(MusicSlot slot)
        {
            if (slot == null)
            {
                return;
            }

            if (slot.Source != null)
            {
                slot.Source.Stop();
                slot.Source.clip = null;
            }

            if (slot.Handle != null)
            {
                slot.Handle.Dispose();
                slot.Handle = null;
            }

            slot.Fade = 0f;
            slot.FadeTarget = 0f;
            slot.Stopping = false;
            slot.InUse = false;
            slot.HasPlayed = false;

            if (ReferenceEquals(m_activeMusic, slot))
            {
                m_activeMusic = null;
            }
        }

        private SfxSlot AcquireSfxSlot(SfxRequest request)
        {
            for (int i = 0; i < m_sfxSlots.Length; i++)
            {
                if (m_sfxSlots[i].Handle == null)
                {
                    return m_sfxSlots[i];
                }
            }

            if (m_options.OverflowPolicy == SfxOverflowPolicy.ReplaceLowestPriority)
            {
                SfxSlot lowest = null;
                for (int i = 0; i < m_sfxSlots.Length; i++)
                {
                    SfxHandle candidate = m_sfxSlots[i].Handle;
                    if (candidate != null && (lowest == null || candidate.Priority < lowest.Handle.Priority))
                    {
                        lowest = m_sfxSlots[i];
                    }
                }

                if (lowest != null && request.Priority > lowest.Handle.Priority)
                {
                    lowest.Handle.StopImmediate();
                    return lowest;
                }
            }

            // DropNewest：池满直接拒绝新请求
            return null;
        }

        private void OnSfxReleased(SfxHandle handle)
        {
            for (int i = 0; i < m_sfxSlots.Length; i++)
            {
                if (ReferenceEquals(m_sfxSlots[i].Handle, handle))
                {
                    m_sfxSlots[i].Handle = null;
                    return;
                }
            }
        }

        private void RaiseMusicChanged(AudioClip clip)
        {
            if (MusicChanged == null)
            {
                return;
            }

            try
            {
                MusicChanged(clip);
            }
            catch (Exception e)
            {
                Log.Error("AudioService", "MusicChanged 回调异常", e);
            }
        }

        private static int CheckBus(AudioBus bus)
        {
            int index = (int)bus;
            if (index < 0 || index > (int)AudioBus.Voice)
            {
                throw new ArgumentOutOfRangeException("bus", bus, "未知音频通道");
            }

            return index;
        }

        private MusicSlot GetSecondaryMusicSlot()
        {
            for (int i = 0; i < m_musicSlots.Length; i++)
            {
                if (!ReferenceEquals(m_musicSlots[i], m_activeMusic))
                {
                    return m_musicSlots[i];
                }
            }

            return m_musicSlots[0];
        }

        private void EnsureInfrastructure()
        {
            if (m_infrastructureReady)
            {
                return;
            }

            if (m_options.Root != null)
            {
                m_root = m_options.Root;
                m_ownsRoot = false;
            }
            else
            {
                m_rootObject = new GameObject(RootObjectName);
                UnityEngine.Object.DontDestroyOnLoad(m_rootObject);
                m_root = m_rootObject.transform;
                m_ownsRoot = true;
            }

            for (int i = 0; i < m_musicSlots.Length; i++)
            {
                m_musicSlots[i] = new MusicSlot(CreateSource(string.Format("Music {0}", i), true));
            }

            for (int i = 0; i < m_sfxSlots.Length; i++)
            {
                m_sfxSlots[i] = new SfxSlot(CreateSource(string.Format("Sfx {0}", i), false));
            }

            m_infrastructureReady = true;
        }

        private AudioSource CreateSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_root, false);

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            return source;
        }

        private sealed class MusicSlot
        {
            public readonly AudioSource Source;

            public IAssetHandle<AudioClip> Handle;
            public float Volume = 1f;
            public float Fade = 1f;
            public float FadeTarget = 1f;
            public float FadeSpeed = 1f;
            public bool Stopping;
            public bool InUse;
            public bool HasPlayed;

            public MusicSlot(AudioSource source)
            {
                Source = source;
            }
        }

        private sealed class SfxSlot
        {
            public readonly AudioSource Source;
            public SfxHandle Handle;

            public SfxSlot(AudioSource source)
            {
                Source = source;
            }
        }

        /// <summary>一次音效播放：负责资源句柄、播放结束检测与池位归还。</summary>
        private sealed class SfxHandle : ISfxHandle
        {
            private readonly AudioService m_owner;
            private readonly SfxSlot m_slot;
            private readonly SfxRequest m_request;
            private readonly CancellationTokenSource m_cts = new CancellationTokenSource();

            private IAssetHandle<AudioClip> m_clipHandle;
            private float m_volume;
            private float m_fadeMultiplier = 1f;
            private float m_stopFadeSeconds;
            private float m_stopFadeRemaining;
            private bool m_started;
            private bool m_hasPlayed;
            private bool m_released;
            private float m_elapsed;

            public SfxHandle(AudioService owner, SfxSlot slot, SfxRequest request)
            {
                m_owner = owner;
                m_slot = slot;
                m_request = request;
                m_volume = Mathf.Clamp01(request.Volume);
            }

            public AudioBus Bus
            {
                get { return m_request.Bus; }
            }

            public int Priority
            {
                get { return m_request.Priority; }
            }

            public bool IsPlaying
            {
                get
                {
                    return !m_released && m_started &&
                           m_slot.Source != null && m_slot.Source.isPlaying;
                }
            }

            public bool IsReleased
            {
                get { return m_released; }
            }

            public float Volume
            {
                get { return m_volume; }
                set
                {
                    m_volume = Mathf.Clamp01(value);
                    ApplyVolume();
                }
            }

            public void Stop(float fadeOutSeconds = 0f)
            {
                if (m_released)
                {
                    return;
                }

                if (m_started && fadeOutSeconds > 0f)
                {
                    m_stopFadeSeconds = fadeOutSeconds;
                    m_stopFadeRemaining = fadeOutSeconds;
                    return;
                }

                StopImmediate();
            }

            public void BeginLoad()
            {
                IAwaitable<IAssetHandle<AudioClip>> loading;
                try
                {
                    loading = m_owner.m_resources.LoadAsync<AudioClip>(m_request.ClipKey, m_cts.Token);
                }
                catch (Exception e)
                {
                    Fail(e);
                    return;
                }

                loading.Completed += delegate
                {
                    IAssetHandle<AudioClip> clipHandle = null;
                    try
                    {
                        clipHandle = loading.GetResult();
                    }
                    catch (Exception e)
                    {
                        if (!m_released)
                        {
                            Fail(e);
                        }

                        return;
                    }

                    // 加载期间已被停止/回收：迟到的结果必须立即释放，不能播放
                    if (m_released)
                    {
                        clipHandle.Dispose();
                        return;
                    }

                    m_clipHandle = clipHandle;
                    Start(clipHandle.Asset);
                };
            }

            public void Start(AudioClip clip)
            {
                if (m_released)
                {
                    if (m_clipHandle != null)
                    {
                        m_clipHandle.Dispose();
                        m_clipHandle = null;
                    }

                    return;
                }

                AudioSource source = m_slot.Source;
                if (source == null || clip == null)
                {
                    Fail(new InvalidOperationException("音效资源或 AudioSource 不可用"));
                    return;
                }

                source.Stop();
                source.clip = clip;
                source.loop = m_request.Loop;
                source.pitch = m_request.Pitch;
                source.spatialBlend = m_request.Spatial
                    ? Mathf.Clamp01(m_request.SpatialBlend <= 0f ? 1f : m_request.SpatialBlend)
                    : 0f;
                source.rolloffMode = m_request.Rolloff;
                source.minDistance = m_request.MinDistance;
                source.maxDistance = m_request.MaxDistance;

                if (m_request.Spatial)
                {
                    source.transform.position = m_request.Position;
                }

                ApplyVolume();
                source.Play();

                m_started = true;
                m_elapsed = 0f;
            }

            public void ReapplyVolume()
            {
                ApplyVolume();
            }

            public void Update(float deltaTime)
            {
                if (m_released)
                {
                    return;
                }

                if (m_stopFadeRemaining > 0f)
                {
                    m_stopFadeRemaining -= deltaTime;
                    if (m_stopFadeRemaining <= 0f)
                    {
                        StopImmediate();
                        return;
                    }

                    m_fadeMultiplier = Mathf.Clamp01(m_stopFadeRemaining / m_stopFadeSeconds);
                    ApplyVolume();
                    return;
                }

                if (!m_started || m_slot.Source == null)
                {
                    return;
                }

                m_elapsed += deltaTime;
                m_hasPlayed |= m_slot.Source.isPlaying;

                if (m_request.Loop)
                {
                    return;
                }

                AudioSource source = m_slot.Source;

                // 播放状态可观测时以 AudioSource 为准（pitch、设备延迟都由引擎自己处理）
                if (m_hasPlayed)
                {
                    if (!source.isPlaying)
                    {
                        StopImmediate();
                    }

                    return;
                }

                // 时长兜底只用于「从未在帧边界观测到 isPlaying」的极短音效
                AudioClip clip = source.clip;
                if (clip == null || clip.length <= 0f)
                {
                    return;
                }

                float pitch = Mathf.Abs(source.pitch);
                if (pitch < 0.01f)
                {
                    // pitch≈0 时播放被冻结，不能按时长兜底回收
                    return;
                }

                // pitch 改变播放速度：pitch 0.5 的实际时长是两倍
                if (m_elapsed > clip.length / pitch + 0.25f)
                {
                    StopImmediate();
                }
            }

            public void StopImmediate()
            {
                if (m_released)
                {
                    return;
                }

                m_released = true;

                if (m_slot.Source != null)
                {
                    m_slot.Source.Stop();
                    m_slot.Source.clip = null;
                }

                if (m_clipHandle != null)
                {
                    m_clipHandle.Dispose();
                    m_clipHandle = null;
                }

                try
                {
                    m_cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                m_owner.OnSfxReleased(this);
            }

            public void Dispose()
            {
                StopImmediate();
            }

            private void ApplyVolume()
            {
                if (m_slot.Source == null)
                {
                    return;
                }

                m_slot.Source.volume =
                    m_volume * m_owner.GetEffectiveVolume(m_request.Bus) * m_fadeMultiplier;
            }

            private void Fail(Exception error)
            {
                Log.Error("AudioService", string.Format("音效播放失败：{0}", m_request), error);
                StopImmediate();
            }
        }
    }
}
