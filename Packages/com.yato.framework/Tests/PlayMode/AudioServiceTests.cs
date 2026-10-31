using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.App;
using Yato.Audio;
using Yato.Core;
using Yato.Resources;

namespace Yato.Tests.PlayMode
{
    /// <summary>
    /// 音频服务集成测试（D06）：通道音量联动、双源交叉渐变、SFX 多实例、
    /// 池满拒绝策略、播放结束/停止回池、资源句柄释放与场景范围绑定。
    /// </summary>
    public sealed class AudioServiceTests
    {
        private readonly List<UnityEngine.Object> m_assets = new List<UnityEngine.Object>();
        private AudioService m_service;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Log.SetLogger(new SilentLogger());

            m_service = new AudioService(NullResourceService.Instance, new AudioServiceOptions
            {
                SfxPoolCapacity = 4,
                DefaultFadeSeconds = 0.05f
            });

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (m_service != null)
            {
                m_service.Shutdown();
                m_service = null;
            }

            for (int i = 0; i < m_assets.Count; i++)
            {
                if (m_assets[i] != null)
                {
                    UnityEngine.Object.Destroy(m_assets[i]);
                }
            }

            m_assets.Clear();

            yield return null;
            Log.SetLogger(null);
        }

        [UnityTest]
        public IEnumerator SetBusVolume_CombinesWithMasterAndRaisesEvent()
        {
            AudioBus changedBus = AudioBus.Master;
            float changedValue = 0f;
            m_service.BusVolumeChanged += delegate(AudioBus bus, float value)
            {
                changedBus = bus;
                changedValue = value;
            };

            m_service.SetBusVolume(AudioBus.Master, 0.5f);
            m_service.SetBusVolume(AudioBus.Sfx, 0.5f);
            m_service.SetBusVolume(AudioBus.Ui, 0.3f);

            Assert.AreEqual(0.5f, m_service.MasterVolume, 0.0001f);
            Assert.AreEqual(0.5f, m_service.GetBusVolume(AudioBus.Sfx), 0.0001f);
            Assert.AreEqual(0.25f, m_service.GetEffectiveVolume(AudioBus.Sfx), 0.0001f);
            Assert.AreEqual(1f, m_service.GetBusVolume(AudioBus.Music), 0.0001f);
            Assert.AreEqual(AudioBus.Ui, changedBus);
            Assert.AreEqual(0.3f, changedValue, 0.0001f);

            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayMusic_CrossfadesToNewClip()
        {
            AudioClip first = CreateClip("MusicA", 1f);
            AudioClip second = CreateClip("MusicB", 1f);

            m_service.PlayMusic(first, new MusicOptions { FadeSeconds = 0.02f });
            yield return null;

            Assert.AreEqual(first, m_service.CurrentMusic);
            Assert.IsTrue(m_service.IsMusicPlaying);

            m_service.PlayMusic(second, new MusicOptions { FadeSeconds = 0.05f });
            yield return new WaitForSeconds(0.2f);

            Assert.AreEqual(second, m_service.CurrentMusic, "交叉渐变后应切到新音乐");
            Assert.IsTrue(m_service.IsMusicPlaying);
            Assert.AreEqual(1f, m_service.CurrentMusicVolume, 0.02f, "渐变完成后音量应到目标值");
        }

        [UnityTest]
        public IEnumerator PauseAndResumeMusic()
        {
            m_service.PlayMusic(CreateClip("Music", 1f), new MusicOptions { FadeSeconds = 0f });
            yield return null;

            Assert.IsTrue(m_service.IsMusicPlaying);

            m_service.PauseMusic();
            Assert.IsTrue(m_service.IsMusicPaused);
            Assert.IsFalse(m_service.IsMusicPlaying);

            m_service.ResumeMusic();
            Assert.IsFalse(m_service.IsMusicPaused);
            Assert.IsTrue(m_service.IsMusicPlaying);
        }

        [UnityTest]
        public IEnumerator StopMusic_FadesOutAndClearsState()
        {
            m_service.PlayMusic(CreateClip("Music", 1f), new MusicOptions { FadeSeconds = 0f });
            yield return null;

            m_service.StopMusic(0.05f);
            Assert.IsNull(m_service.CurrentMusic);

            yield return new WaitForSeconds(0.2f);

            Assert.IsFalse(m_service.IsMusicPlaying);
        }

        [UnityTest]
        public IEnumerator Sfx_MultipleInstancesPlayConcurrently()
        {
            AudioClip clip = CreateClip("Hit", 1f);

            ISfxHandle first;
            ISfxHandle second;
            Assert.IsTrue(m_service.TryPlaySfx(new SfxRequest(clip) { Loop = true }, out first));
            Assert.IsTrue(m_service.TryPlaySfx(new SfxRequest(clip) { Loop = true }, out second));

            Assert.AreEqual(2, m_service.ActiveSfxCount);
            Assert.IsTrue(first.IsPlaying);
            Assert.IsTrue(second.IsPlaying);

            first.Stop();
            Assert.IsTrue(first.IsReleased);
            Assert.AreEqual(1, m_service.ActiveSfxCount);

            second.Stop();
            Assert.AreEqual(0, m_service.ActiveSfxCount);

            yield return null;
        }

        [UnityTest]
        public IEnumerator Sfx_PoolFull_DropsNewestByDefault()
        {
            var small = new AudioService(NullResourceService.Instance, new AudioServiceOptions
            {
                SfxPoolCapacity = 1
            });

            AudioClip clip = CreateClip("Loop", 1f);
            ISfxHandle first;
            ISfxHandle second;

            Assert.IsTrue(small.TryPlaySfx(new SfxRequest(clip) { Loop = true }, out first));
            Assert.IsFalse(small.TryPlaySfx(new SfxRequest(clip) { Loop = true }, out second));
            Assert.IsNull(second);
            Assert.AreEqual(1, small.ActiveSfxCount);

            small.Shutdown();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Sfx_PoolFull_ReplacesLowestPriorityWhenConfigured()
        {
            var small = new AudioService(NullResourceService.Instance, new AudioServiceOptions
            {
                SfxPoolCapacity = 1,
                OverflowPolicy = SfxOverflowPolicy.ReplaceLowestPriority
            });

            AudioClip clip = CreateClip("Loop", 1f);
            ISfxHandle low;
            ISfxHandle high;
            ISfxHandle medium;

            Assert.IsTrue(small.TryPlaySfx(new SfxRequest(clip) { Loop = true, Priority = 1 }, out low));
            Assert.IsTrue(small.TryPlaySfx(new SfxRequest(clip) { Loop = true, Priority = 5 }, out high));
            Assert.IsTrue(low.IsReleased, "低优先级实例应被抢占回池");

            Assert.IsFalse(
                small.TryPlaySfx(new SfxRequest(clip) { Loop = true, Priority = 2 }, out medium),
                "低优先级请求不能抢占更高优先级实例");
            Assert.IsNull(medium);

            small.Shutdown();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Sfx_NonLooping_ReleasesAfterFinish()
        {
            AudioClip clip = CreateClip("Blip", 0.05f);

            ISfxHandle handle;
            Assert.IsTrue(m_service.TryPlaySfx(new SfxRequest(clip), out handle));

            yield return new WaitForSeconds(0.4f);

            Assert.IsTrue(handle.IsReleased);
            Assert.AreEqual(0, m_service.ActiveSfxCount);
        }

        [UnityTest]
        public IEnumerator Sfx_Stop_ReleasesSlotImmediately()
        {
            AudioClip clip = CreateClip("Loop", 1f);

            ISfxHandle handle;
            Assert.IsTrue(m_service.TryPlaySfx(new SfxRequest(clip) { Loop = true }, out handle));

            handle.Stop();

            Assert.IsTrue(handle.IsReleased);
            Assert.AreEqual(0, m_service.ActiveSfxCount);

            yield return null;
        }

        [UnityTest]
        public IEnumerator Sfx_FromResourceKey_DisposesClipHandleOnStop()
        {
            var resources = new FakeResourceService();
            resources.Clips.Enqueue(CreateClip("Loaded", 1f));

            var service = new AudioService(resources, new AudioServiceOptions { SfxPoolCapacity = 2 });

            ISfxHandle handle;
            Assert.IsTrue(service.TryPlaySfx(
                new SfxRequest(new AssetKey("audio/hit")) { Loop = true }, out handle));

            Assert.AreEqual(1, resources.LoadCalls);
            Assert.AreEqual(1, resources.Handles.Count);
            Assert.IsTrue(handle.IsPlaying);

            handle.Stop();

            Assert.AreEqual(1, resources.Handles[0].DisposeCalls, "停止后必须释放 AudioClip 句柄");
            Assert.AreEqual(0, service.ActiveSfxCount);

            service.Shutdown();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Sfx_StopWhileLoading_DisposesLateResult()
        {
            var resources = new FakeResourceService { Defer = true };
            resources.Clips.Enqueue(CreateClip("Late", 1f));

            var service = new AudioService(resources, new AudioServiceOptions { SfxPoolCapacity = 2 });

            ISfxHandle handle;
            Assert.IsTrue(service.TryPlaySfx(new SfxRequest(new AssetKey("audio/late")), out handle));

            handle.Stop();
            Assert.IsTrue(handle.IsReleased);
            Assert.AreEqual(0, service.ActiveSfxCount);

            // 迟到的加载结果必须被释放，不能播放
            resources.CompletePending();
            yield return null;

            Assert.AreEqual(1, resources.Handles.Count);
            Assert.AreEqual(1, resources.Handles[0].DisposeCalls);
            Assert.AreEqual(0, service.ActiveSfxCount);

            service.Shutdown();
        }

        [UnityTest]
        public IEnumerator Music_FromResourceKey_ReleasesPreviousHandle()
        {
            var resources = new FakeResourceService();
            resources.Clips.Enqueue(CreateClip("SongA", 1f));
            resources.Clips.Enqueue(CreateClip("SongB", 1f));

            var service = new AudioService(resources, new AudioServiceOptions { SfxPoolCapacity = 2 });

            Await(service.PlayMusicAsync(new AssetKey("music/a"), new MusicOptions { FadeSeconds = 0f }));
            yield return null;

            Await(service.PlayMusicAsync(new AssetKey("music/b"), new MusicOptions { FadeSeconds = 0.05f }));
            yield return new WaitForSeconds(0.2f);

            Assert.AreEqual(2, resources.Handles.Count);
            Assert.AreEqual(1, resources.Handles[0].DisposeCalls, "被替换的音乐句柄必须释放");

            service.StopMusic(0f);
            yield return null;

            Assert.AreEqual(1, resources.Handles[1].DisposeCalls);

            service.Shutdown();
        }

        [UnityTest]
        public IEnumerator Sfx_SceneScopeRelease_StopsLoopingSound()
        {
            var scope = new ResourceScope("scene-test");
            AudioClip clip = CreateClip("SceneLoop", 1f);

            ISfxHandle handle;
            Assert.IsTrue(m_service.TryPlaySfx(new SfxRequest(clip) { Loop = true }, out handle));

            scope.Track(handle);
            Assert.IsFalse(handle.IsReleased);

            scope.ReleaseAll();

            Assert.IsTrue(handle.IsReleased, "场景范围释放时必须停止场景循环音效");
            Assert.AreEqual(0, m_service.ActiveSfxCount);

            yield return null;
        }

        [UnityTest]
        public IEnumerator Shutdown_ReleasesEverything()
        {
            var resources = new FakeResourceService();
            resources.Clips.Enqueue(CreateClip("Music", 1f));
            resources.Clips.Enqueue(CreateClip("Sfx", 1f));

            var service = new AudioService(resources, new AudioServiceOptions { SfxPoolCapacity = 2 });

            Await(service.PlayMusicAsync(new AssetKey("music/x"), new MusicOptions { FadeSeconds = 0f }));

            ISfxHandle handle;
            Assert.IsTrue(service.TryPlaySfx(
                new SfxRequest(new AssetKey("audio/x")) { Loop = true }, out handle));

            service.Shutdown();

            Assert.IsTrue(handle.IsReleased);
            Assert.AreEqual(0, service.ActiveSfxCount);
            Assert.IsFalse(service.IsMusicPlaying);
            Assert.AreEqual(2, resources.Handles.Count);
            for (int i = 0; i < resources.Handles.Count; i++)
            {
                Assert.AreEqual(1, resources.Handles[i].DisposeCalls);
            }

            yield return null;
        }

        private AudioClip CreateClip(string name, float seconds)
        {
            int sampleRate = 44100;
            int samples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
            AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(new float[samples], 0);
            m_assets.Add(clip);
            return clip;
        }

        private static T Await<T>(IAwaitable<T> awaitable)
        {
            Assert.IsTrue(awaitable.IsDone, "存根资源应立即完成");
            return awaitable.GetResult();
        }

        /// <summary>可控加载时机、记录句柄的 IResourceService 存根。</summary>
        private sealed class FakeResourceService : IResourceService
        {
            private readonly List<Action> m_pending = new List<Action>();

            public readonly Queue<AudioClip> Clips = new Queue<AudioClip>();
            public readonly List<FakeHandleBase> Handles = new List<FakeHandleBase>();

            public bool Defer;
            public int LoadCalls;

            public ResourceStats Stats
            {
                get { return ResourceStats.Empty; }
            }

            public IAssetHandle<T> Load<T>(AssetKey key) where T : UnityEngine.Object
            {
                throw new ResourceException(key.ToString(), null, ResourceStage.ProviderSelect, "存根不支持同步加载");
            }

            public IAwaitable<IAssetHandle<T>> LoadAsync<T>(
                AssetKey key,
                CancellationToken ct = default(CancellationToken))
                where T : UnityEngine.Object
            {
                LoadCalls++;

                var source = new Yato.Resources.AwaitableCompletionSource<IAssetHandle<T>>();
                Action complete = delegate
                {
                    AudioClip clip = Clips.Count > 0 ? Clips.Dequeue() : null;
                    var handle = new FakeHandle<T>(key, clip as T);
                    Handles.Add(handle);
                    source.SetResult(handle);
                };

                if (Defer)
                {
                    m_pending.Add(complete);
                }
                else
                {
                    complete();
                }

                return source;
            }

            public void CompletePending()
            {
                Action[] pending = m_pending.ToArray();
                m_pending.Clear();

                for (int i = 0; i < pending.Length; i++)
                {
                    pending[i]();
                }
            }

            public IAwaitable<IAssetHandle<GameObject>> InstantiateAsync(
                AssetKey key,
                Transform parent,
                CancellationToken ct = default(CancellationToken))
            {
                throw new ResourceException(key.ToString(), null, ResourceStage.ProviderSelect, "存根不支持实例化");
            }

            public IAwaitable PreloadAsync(
                IReadOnlyList<AssetKey> keys,
                IProgress<float> progress = null,
                CancellationToken ct = default(CancellationToken))
            {
                throw new ResourceException(null, null, ResourceStage.ProviderSelect, "存根不支持预加载");
            }

            public void Release(IAssetHandle handle)
            {
                if (handle != null)
                {
                    handle.Dispose();
                }
            }

            public void ReleaseUnused()
            {
            }
        }

        private abstract class FakeHandleBase
        {
            public int DisposeCalls { get; protected set; }

            public bool IsDisposed
            {
                get { return DisposeCalls > 0; }
            }
        }

        private sealed class FakeHandle<T> : FakeHandleBase, IAssetHandle<T> where T : UnityEngine.Object
        {
            private T m_asset;

            public FakeHandle(AssetKey key, T asset)
            {
                Key = key;
                m_asset = asset;
            }

            public AssetKey Key { get; private set; }

            public T Asset
            {
                get { return m_asset; }
            }

            UnityEngine.Object IAssetHandle.Asset
            {
                get { return m_asset; }
            }

            public bool IsDone
            {
                get { return true; }
            }

            public bool IsValid
            {
                get { return m_asset != null; }
            }

            public int RefCount
            {
                get { return 1; }
            }

            public void Dispose()
            {
                DisposeCalls++;
                m_asset = null;
            }
        }

        private sealed class SilentLogger : Yato.Core.ILogger
        {
            public void Debug(string message)
            {
            }

            public void Info(string message)
            {
            }

            public void Warn(string message)
            {
            }

            public void Error(string message, Exception exception = null)
            {
            }
        }
    }
}
