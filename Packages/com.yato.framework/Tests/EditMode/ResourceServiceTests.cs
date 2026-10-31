using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using Yato.Core;
using Yato.Resources;

namespace Yato.Tests.EditMode
{
    /// <summary>
    /// 资源引用计数回归测试（文档 §5.1 风险线索 A）。
    /// 重点锁定「Provider 引用与句柄引用必须一一对应」：多持有者、取消、类型不符都不能泄漏。
    /// </summary>
    public sealed class ResourceServiceTests
    {
        private static readonly AssetKey Key = new AssetKey("ui/test/panel");

        private ScriptableObject m_asset;
        private FakeProvider m_provider;
        private ResourceService m_service;

        [SetUp]
        public void SetUp()
        {
            Log.SetLogger(new SilentLogger());

            m_asset = ScriptableObject.CreateInstance<ScriptableObject>();
            m_provider = new FakeProvider(m_asset);
            m_service = new ResourceService(m_provider);
        }

        [TearDown]
        public void TearDown()
        {
            Log.SetLogger(null);

            if (m_asset != null)
            {
                UnityEngine.Object.DestroyImmediate(m_asset);
                m_asset = null;
            }
        }

        [Test]
        public void LoadAsync_SameKeyTwice_ReusesProviderReference()
        {
            IAssetHandle<ScriptableObject> first = Await(m_service.LoadAsync<ScriptableObject>(Key));
            IAssetHandle<ScriptableObject> second = Await(m_service.LoadAsync<ScriptableObject>(Key));

            Assert.AreEqual(1, m_provider.LoadCalls, "同一 key 的重复加载不应再次向 Provider 申请引用");
            Assert.AreEqual(2, first.RefCount, "RefCount 必须反映真实持有者数量，而不是创建时快照");
            Assert.AreEqual(2, second.RefCount);

            first.Dispose();
            Assert.AreEqual(1, second.RefCount, "一个持有者释放后计数应下降");
            Assert.AreEqual(0, m_provider.ReleaseCalls, "仍有持有者时不能归还 Provider 引用");

            second.Dispose();
            Assert.AreEqual(1, m_provider.ReleaseCalls, "最后一个持有者释放时只归还一次");
            Assert.AreEqual(0, m_service.ActiveAssetCount);
        }

        [Test]
        public void LoadAsync_ConcurrentSameKey_SharesOneProviderLoad()
        {
            m_provider.AutoComplete = false;

            IAwaitable<IAssetHandle<ScriptableObject>> first = m_service.LoadAsync<ScriptableObject>(Key);
            IAwaitable<IAssetHandle<ScriptableObject>> second = m_service.LoadAsync<ScriptableObject>(Key);

            Assert.AreEqual(1, m_provider.LoadCalls, "并发加载必须单飞");

            m_provider.CompleteAll();

            IAssetHandle<ScriptableObject> handleA = first.GetResult();
            IAssetHandle<ScriptableObject> handleB = second.GetResult();

            Assert.AreEqual(2, handleA.RefCount);
            Assert.AreEqual(2, handleB.RefCount);

            handleA.Dispose();
            handleB.Dispose();

            Assert.AreEqual(1, m_provider.ReleaseCalls, "两个持有者都释放后只归还一次 Provider 引用");
        }

        [Test]
        public void LoadAsync_CancelledBeforeConsumption_ReleasesProviderReference()
        {
            m_provider.AutoComplete = false;
            var cts = new CancellationTokenSource();

            IAwaitable<IAssetHandle<ScriptableObject>> loading =
                m_service.LoadAsync<ScriptableObject>(Key, cts.Token);

            Assert.IsFalse(loading.IsDone);

            cts.Cancel();
            m_provider.CompleteAll();

            Assert.IsTrue(loading.IsDone);
            Assert.Throws<OperationCanceledException>(delegate { loading.GetResult(); });
            Assert.AreEqual(1, m_provider.ReleaseCalls, "没有任何消费者接管时必须回滚 Provider 引用");
            Assert.AreEqual(0, m_service.ActiveAssetCount);
        }

        [Test]
        public void LoadAsync_FirstConsumerCancels_SecondStillReceivesAsset()
        {
            m_provider.AutoComplete = false;
            var cts = new CancellationTokenSource();

            IAwaitable<IAssetHandle<ScriptableObject>> first =
                m_service.LoadAsync<ScriptableObject>(Key, cts.Token);
            IAwaitable<IAssetHandle<ScriptableObject>> second =
                m_service.LoadAsync<ScriptableObject>(Key);

            Assert.AreEqual(1, m_provider.LoadCalls, "同一 key 仍然只向 Provider 加载一次");

            cts.Cancel();

            Assert.IsFalse(
                m_provider.LastToken.IsCancellationRequested,
                "单个消费者取消不能中止共享的底层加载");

            m_provider.CompleteAll();

            Assert.Throws<OperationCanceledException>(delegate { first.GetResult(); });

            IAssetHandle<ScriptableObject> handle = second.GetResult();
            Assert.IsNotNull(handle);
            Assert.AreEqual(m_asset, handle.Asset, "未取消的消费者必须拿到资源");
            Assert.AreEqual(1, handle.RefCount);

            handle.Dispose();
            Assert.AreEqual(1, m_provider.ReleaseCalls);
            Assert.AreEqual(0, m_service.ActiveAssetCount);

            cts.Dispose();
        }

        [Test]
        public void LoadAsync_AllConsumersCancel_AbortsProviderLoad()
        {
            m_provider.AutoComplete = false;
            var firstCts = new CancellationTokenSource();
            var secondCts = new CancellationTokenSource();

            IAwaitable<IAssetHandle<ScriptableObject>> first =
                m_service.LoadAsync<ScriptableObject>(Key, firstCts.Token);
            IAwaitable<IAssetHandle<ScriptableObject>> second =
                m_service.LoadAsync<ScriptableObject>(Key, secondCts.Token);

            firstCts.Cancel();
            Assert.IsFalse(m_provider.LastToken.IsCancellationRequested, "仍有消费者时不中止加载");

            secondCts.Cancel();
            Assert.IsTrue(m_provider.LastToken.IsCancellationRequested, "全部消费者取消后才中止底层加载");

            m_provider.CompleteCancelled();

            Assert.Throws<OperationCanceledException>(delegate { first.GetResult(); });
            Assert.Throws<OperationCanceledException>(delegate { second.GetResult(); });
            Assert.AreEqual(0, m_service.ActiveAssetCount);

            firstCts.Dispose();
            secondCts.Dispose();
        }

        [Test]
        public void Load_SyncUnsupported_CancelsAndReleasesLateResult()
        {
            m_provider.AutoComplete = false;

            Assert.Throws<ResourceException>(delegate { m_service.Load<ScriptableObject>(Key); });

            Assert.IsTrue(m_provider.LastToken.IsCancellationRequested, "无法同步返回时必须取消 Provider 加载");

            // Provider 不响应取消并最终成功：无人接管的引用必须归还
            m_provider.CompleteAll();

            Assert.AreEqual(1, m_provider.ReleaseCalls);
            Assert.AreEqual(0, m_service.ActiveAssetCount);
        }

        [Test]
        public void Load_SyncTypeMismatch_ReleasesProviderReference()
        {
            Assert.Throws<ResourceException>(delegate { m_service.Load<GameObject>(Key); });

            Assert.AreEqual(1, m_provider.ReleaseCalls, "同步加载无法建立句柄时也必须归还 Provider 引用");
            Assert.AreEqual(0, m_service.ActiveAssetCount);
        }

        [Test]
        public void LoadAsync_TypeMismatch_ReleasesProviderReference()
        {
            IAwaitable<IAssetHandle<GameObject>> loading = m_service.LoadAsync<GameObject>(Key);

            Assert.Throws<ResourceException>(delegate { loading.GetResult(); });
            Assert.AreEqual(1, m_provider.ReleaseCalls, "类型不符的加载结果必须归还 Provider 引用");
            Assert.AreEqual(0, m_service.ActiveAssetCount);
        }

        [Test]
        public void Load_ReturnsCachedEntryAndDoubleDisposeIsIdempotent()
        {
            IAssetHandle<ScriptableObject> handle = m_service.Load<ScriptableObject>(Key);

            Assert.AreEqual(1, m_provider.LoadCalls);
            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(1, handle.RefCount);

            handle.Dispose();
            handle.Dispose();

            Assert.AreEqual(1, m_provider.ReleaseCalls, "重复 Dispose 只能归还一次");
            Assert.AreEqual(0, m_service.ActiveAssetCount);
        }

        [Test]
        public void ActiveAssetCount_ReturnsToZero_AfterAllHandlesReleased()
        {
            var handles = new List<IAssetHandle<ScriptableObject>>();
            for (int i = 0; i < 4; i++)
            {
                handles.Add(m_service.Load<ScriptableObject>(Key));
            }

            Assert.AreEqual(1, m_provider.LoadCalls, "同一 key 只加载一次");
            Assert.AreEqual(4, handles[0].RefCount, "RefCount 等于当前持有者数量（4 个句柄）");
            Assert.AreEqual(1, m_service.ActiveAssetCount);

            for (int i = 0; i < handles.Count; i++)
            {
                handles[i].Dispose();
            }

            Assert.AreEqual(0, m_service.ActiveAssetCount);
            Assert.AreEqual(1, m_provider.ReleaseCalls);
        }

        private static T Await<T>(IAwaitable<T> awaitable)
        {
            Assert.IsTrue(awaitable.IsDone, "FakeProvider 应同步完成");
            return awaitable.GetResult();
        }

        /// <summary>可控制完成时机、记录调用次数的 Provider 存根。</summary>
        private sealed class FakeProvider : IResourceProvider
        {
            private readonly UnityEngine.Object m_asset;
            private readonly List<Yato.Resources.AwaitableCompletionSource<UnityEngine.Object>> m_pending =
                new List<Yato.Resources.AwaitableCompletionSource<UnityEngine.Object>>();

            public bool AutoComplete = true;
            public int LoadCalls;
            public int ReleaseCalls;

            /// <summary>最近一次传给 Provider 的取消令牌（用于验证 Provider 级取消语义）。</summary>
            public CancellationToken LastToken;

            public FakeProvider(UnityEngine.Object asset)
            {
                m_asset = asset;
            }

            public ResourceStats Stats
            {
                get { return ResourceStats.Empty; }
            }

            public IAwaitable<UnityEngine.Object> LoadAsync(AssetKey key, Type type, CancellationToken ct)
            {
                LoadCalls++;
                LastToken = ct;

                var source = new Yato.Resources.AwaitableCompletionSource<UnityEngine.Object>();
                if (AutoComplete)
                {
                    source.SetResult(m_asset);
                }
                else
                {
                    m_pending.Add(source);
                }

                return source;
            }

            public void CompleteAll()
            {
                Yato.Resources.AwaitableCompletionSource<UnityEngine.Object>[] pending = m_pending.ToArray();
                m_pending.Clear();
                for (int i = 0; i < pending.Length; i++)
                {
                    pending[i].SetResult(m_asset);
                }
            }

            public void CompleteCancelled()
            {
                Yato.Resources.AwaitableCompletionSource<UnityEngine.Object>[] pending = m_pending.ToArray();
                m_pending.Clear();
                for (int i = 0; i < pending.Length; i++)
                {
                    pending[i].SetException(new OperationCanceledException());
                }
            }

            public void Release(AssetKey key)
            {
                ReleaseCalls++;
            }

            public void ReleaseUnused()
            {
            }
        }

        /// <summary>测试期静默日志，避免预期内的失败路径污染控制台断言。</summary>
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
