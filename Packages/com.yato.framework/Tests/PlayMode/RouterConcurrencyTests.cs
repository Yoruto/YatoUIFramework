using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.Core;
using Yato.Resources;
using Yato.UI;

namespace Yato.Tests.PlayMode
{
    /// <summary>
    /// 页面并发与缓存所有权回归测试（文档 §5.1 风险线索 B/C）：
    /// 同一页面加载中重复打开只能产生一个实例；缓存页的预制体句柄必须恰好释放一次。
    /// </summary>
    public sealed class RouterConcurrencyTests
    {
        private readonly List<GameObject> m_objects = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Log.SetLogger(new SilentLogger());
            UIRouter.CloseAll();
            UIRouter.Configure(NullResourceService.Instance, null);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            UIRouter.Configure(NullResourceService.Instance, null);
            UIRouter.CloseAll();
            Log.SetLogger(null);

            for (int i = 0; i < m_objects.Count; i++)
            {
                if (m_objects[i] != null)
                {
                    UnityEngine.Object.Destroy(m_objects[i]);
                }
            }

            m_objects.Clear();
        }

        [UnityTest]
        public IEnumerator OpenAsync_SingletonTwiceWhileLoading_CreatesSingleInstance()
        {
            var resources = new FakeResourceService(CreatePrefab("Panel")) { AutoComplete = false };
            UIRouter.Configure(resources, null);

            var def = new UIPageDef("Panel", null, new AssetKey("ui/panel"));

            IAwaitable<UIPageHandle> first = UIRouter.OpenAsync(def);
            IAwaitable<UIPageHandle> second = UIRouter.OpenAsync(def);

            Assert.AreEqual(1, resources.LoadCalls, "加载中的重复打开必须单飞，不能发起第二次加载");
            Assert.IsFalse(first.IsDone);

            resources.CompleteAll();
            yield return null;

            Assert.IsTrue(first.IsDone);
            Assert.IsTrue(second.IsDone);

            UIPageHandle a = first.GetResult();
            UIPageHandle b = second.GetResult();

            Assert.AreSame(a, b, "两次调用应共享同一句柄");
            Assert.AreEqual(1, UIRouter.Stack.GetHandles(UILayer.Normal).Count, "栈内只能有一个页面实例");
            Assert.AreEqual(1, resources.LoadCalls);
        }

        [UnityTest]
        public IEnumerator CachedPage_OpenCloseCycles_DisposesPrefabHandleOnce()
        {
            var resources = new FakeResourceService(CreatePrefab("Bag"));
            UIRouter.Configure(resources, null);

            var def = new UIPageDef("Bag", null, new AssetKey("ui/bag"), UILayer.Normal, true, true);

            UIPageHandle first = Await(UIRouter.OpenAsync(def));
            UIRouter.Close(first);

            Assert.IsTrue(UIRouter.IsCached("Bag"), "可缓存页面关闭后应进入缓存");
            Assert.AreEqual(0, resources.DisposedHandles, "缓存期间继续持有预制体句柄");

            UIPageHandle second = Await(UIRouter.OpenAsync(def));

            Assert.IsFalse(UIRouter.IsCached("Bag"), "恢复后不能同时留在缓存，否则重复关闭会覆盖并泄漏");
            Assert.AreEqual(1, resources.LoadCalls, "缓存恢复不应重新加载预制体");

            UIRouter.Close(second);
            Assert.AreEqual(0, resources.DisposedHandles);

            UIRouter.CloseAll();
            yield return null;

            Assert.AreEqual(1, resources.DisposedHandles, "CloseAll 清理缓存时恰好释放一次");
        }

        [UnityTest]
        public IEnumerator OpenAsync_InvalidLayer_FailsWithoutLeavingState()
        {
            var resources = new FakeResourceService(CreatePrefab("Bad"));
            UIRouter.Configure(resources, null);

            var def = new UIPageDef("Bad", null, new AssetKey("ui/bad"), (UILayer)99);
            IAwaitable<UIPageHandle> opening = UIRouter.OpenAsync(def);

            yield return null;

            Assert.IsTrue(opening.IsDone);
            Assert.Throws<ArgumentOutOfRangeException>(delegate { opening.GetResult(); });
            Assert.IsFalse(UIRouter.IsOpen("Bad"), "失败不应留下打开状态");
            Assert.AreEqual(0, UIRouter.Stack.GetHandles(UILayer.Normal).Count);
            Assert.AreEqual(0, resources.DisposedHandles, "未创建实例时不应产生句柄释放");

            // 失败后加载计数已清理，后续正常打开仍然可用
            var valid = new UIPageDef("Good", null, new AssetKey("ui/good"));
            UIPageHandle good = Await(UIRouter.OpenAsync(valid));
            Assert.IsNotNull(good);
            Assert.IsTrue(UIRouter.IsOpen("Good"));
        }

        [UnityTest]
        public IEnumerator Close_StaleHandle_DoesNotDestroyActivePage()
        {
            var resources = new FakeResourceService(CreatePrefab("Keep"));
            UIRouter.Configure(resources, null);

            var def = new UIPageDef("Keep", null, new AssetKey("ui/keep"), UILayer.Normal, true, true);

            UIPageHandle first = Await(UIRouter.OpenAsync(def));
            UIRouter.Close(first);

            UIPageHandle second = Await(UIRouter.OpenAsync(def));
            Assert.IsTrue(UIRouter.IsOpen("Keep"));

            // 旧句柄已经失效，重复关闭不能销毁正在显示的实例
            UIRouter.Close(first);

            Assert.IsTrue(UIRouter.IsOpen("Keep"));
            Assert.IsTrue(second.IsValid);
            Assert.AreEqual(0, resources.DisposedHandles);

            yield return null;
        }

        private GameObject CreatePrefab(string name)
        {
            var prefab = new GameObject("~prefab-" + name);
            prefab.AddComponent<UIPageView>();
            m_objects.Add(prefab);
            return prefab;
        }

        private static UIPageHandle Await(IAwaitable<UIPageHandle> awaitable)
        {
            Assert.IsTrue(awaitable.IsDone, "FakeResourceService 应立即完成");
            return awaitable.GetResult();
        }

        /// <summary>返回场景内 GameObject 作为预制体的资源服务存根，可延迟完成。</summary>
        private sealed class FakeResourceService : IResourceService
        {
            private readonly GameObject m_prefab;
            private readonly List<Action> m_deferred = new List<Action>();

            public bool AutoComplete = true;
            public int LoadCalls;
            public int DisposedHandles;

            public FakeResourceService(GameObject prefab)
            {
                m_prefab = prefab;
            }

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
                    if (ct.IsCancellationRequested)
                    {
                        source.SetException(new OperationCanceledException());
                        return;
                    }

                    var handle = new FakeHandle<T>(
                        key,
                        m_prefab as T,
                        delegate { DisposedHandles++; });
                    source.SetResult(handle);
                };

                if (AutoComplete)
                {
                    complete();
                }
                else
                {
                    m_deferred.Add(complete);
                }

                return source;
            }

            public void CompleteAll()
            {
                Action[] deferred = m_deferred.ToArray();
                m_deferred.Clear();
                for (int i = 0; i < deferred.Length; i++)
                {
                    deferred[i]();
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

        private sealed class FakeHandle<T> : IAssetHandle<T> where T : UnityEngine.Object
        {
            private readonly AssetKey m_key;
            private readonly Action m_onDispose;
            private T m_asset;
            private bool m_disposed;

            public FakeHandle(AssetKey key, T asset, Action onDispose)
            {
                m_key = key;
                m_asset = asset;
                m_onDispose = onDispose;
            }

            public T Asset
            {
                get { return m_disposed ? null : m_asset; }
            }

            UnityEngine.Object IAssetHandle.Asset
            {
                get { return Asset; }
            }

            public AssetKey Key
            {
                get { return m_key; }
            }

            public bool IsDone
            {
                get { return true; }
            }

            public bool IsValid
            {
                get { return !m_disposed && m_asset != null; }
            }

            public int RefCount
            {
                get { return m_disposed ? 0 : 1; }
            }

            public void Dispose()
            {
                if (m_disposed)
                {
                    return;
                }

                m_disposed = true;
                m_asset = null;

                if (m_onDispose != null)
                {
                    m_onDispose();
                }
            }
        }

        /// <summary>测试期静默日志，避免预期内的失败路径触发 LogAssert。</summary>
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
