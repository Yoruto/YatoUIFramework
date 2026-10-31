using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Yato.App;
using Yato.Core;
using Yato.Resources;

namespace Yato.Tests.PlayMode
{
    /// <summary>
    /// SceneService 集成测试（D03）：Single/Additive、互斥通道、取消、失败、
    /// Loaded/Ready、初始化器超时与场景资源释放。
    /// 使用假后端 + 运行时创建的空场景，不依赖 Build Settings。
    /// </summary>
    public sealed class SceneServiceTests
    {
        private SceneService m_service;
        private FakeSceneBackend m_backend;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Log.SetLogger(new SilentLogger());
            m_backend = new FakeSceneBackend();
            m_service = new SceneService(1f, m_backend);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            m_service.Shutdown();
            yield return m_backend.UnloadAll();
            Log.SetLogger(null);
        }

        [UnityTest]
        public IEnumerator LoadAsync_Single_BecomesReadyAndRaisesEventsInOrder()
        {
            var events = new List<string>();
            m_service.LoadStarted += delegate(SceneLoadRequest request) { events.Add("started"); };
            m_service.ContextLoaded += delegate(SceneContext context) { events.Add("loaded"); };
            m_service.ContextReady += delegate(SceneContext context) { events.Add("ready"); };
            m_service.LoadCompleted += delegate(SceneLoadResult result) { events.Add("completed"); };

            var holder = new ResultHolder();
            yield return Load(new SceneLoadRequest("Arena"), holder);

            Assert.AreEqual(SceneLoadStatus.Success, holder.Result.Status);
            Assert.AreEqual(SceneLoadStage.None, holder.Result.Stage);
            Assert.IsNotNull(holder.Result.Context);
            Assert.AreEqual(SceneLoadState.Ready, holder.Result.Context.State);
            Assert.IsTrue(holder.Result.Context.IsReady);
            Assert.AreSame(holder.Result.Context, m_service.ActiveContext);
            Assert.AreEqual(1, m_service.LoadedContexts.Count);
            Assert.IsFalse(m_service.IsBusy);
            CollectionAssert.AreEqual(new[] { "started", "loaded", "ready", "completed" }, events);
        }

        [UnityTest]
        public IEnumerator LoadAsync_Additive_KeepsIndependentContexts()
        {
            var first = new ResultHolder();
            yield return Load(new SceneLoadRequest("Main"), first);

            var second = new ResultHolder();
            yield return Load(new SceneLoadRequest("Overlay", LoadSceneMode.Additive), second);

            Assert.AreEqual(SceneLoadStatus.Success, second.Result.Status);
            Assert.AreEqual(2, m_service.LoadedContexts.Count);
            Assert.AreSame(first.Result.Context, m_service.ActiveContext, "Additive 不应替换活动场景");
            Assert.AreEqual(LoadSceneMode.Additive, second.Result.Context.Mode);
            Assert.IsTrue(second.Result.Context.IsReady);
            Assert.AreNotSame(
                first.Result.Context.Resources,
                second.Result.Context.Resources,
                "每个场景必须拥有独立的资源范围");
        }

        [UnityTest]
        public IEnumerator LoadAsync_WhileBusy_ReturnsBusyImmediately()
        {
            m_backend.AutoComplete = false;

            IAwaitable<SceneLoadResult> first = m_service.LoadAsync(new SceneLoadRequest("Slow"));
            IAwaitable<SceneLoadResult> second = m_service.LoadAsync(new SceneLoadRequest("Another"));

            Assert.IsTrue(second.IsDone, "忙时必须立即返回，不能排队");
            Assert.AreEqual(SceneLoadStatus.Busy, second.GetResult().Status);
            Assert.AreEqual(SceneLoadStage.None, second.GetResult().Stage);
            Assert.IsTrue(m_service.IsBusy);

            while (m_backend.PendingCount == 0)
            {
                yield return null;
            }

            m_backend.CompletePending();

            while (!first.IsDone)
            {
                yield return null;
            }

            Assert.AreEqual(SceneLoadStatus.Success, first.GetResult().Status);
            Assert.AreEqual(1, m_backend.LoadCalls, "被拒绝的请求不能真正开始加载");
            Assert.IsFalse(m_service.IsBusy);
        }

        [UnityTest]
        public IEnumerator LoadAsync_CancelBeforeUnityLoad_ReturnsCanceled()
        {
            var cts = new CancellationTokenSource();
            IAwaitable<SceneLoadResult> opening =
                m_service.LoadAsync(new SceneLoadRequest("CancelMe").WithCancellation(cts.Token));

            Assert.IsTrue(m_service.IsBusy);
            cts.Cancel();

            while (!opening.IsDone)
            {
                yield return null;
            }

            SceneLoadResult result = opening.GetResult();
            Assert.AreEqual(SceneLoadStatus.Canceled, result.Status);
            Assert.AreEqual(SceneLoadStage.CancelCheck, result.Stage);
            Assert.AreEqual(0, m_backend.LoadCalls, "取消发生在 Unity 加载之前，不应调用后端");
            Assert.IsFalse(m_service.IsBusy);

            cts.Dispose();
        }

        [UnityTest]
        public IEnumerator LoadAsync_AlreadyCanceled_DoesNotTakeBusy()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            IAwaitable<SceneLoadResult> opening =
                m_service.LoadAsync(new SceneLoadRequest("Nope").WithCancellation(cts.Token));

            Assert.IsTrue(opening.IsDone);
            Assert.AreEqual(SceneLoadStatus.Canceled, opening.GetResult().Status);
            Assert.IsFalse(m_service.IsBusy);
            Assert.AreEqual(0, m_backend.LoadCalls);

            cts.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator LoadAsync_InvalidScene_ReturnsInvalidScene()
        {
            m_backend.CanLoadResult = false;

            IAwaitable<SceneLoadResult> opening = m_service.LoadAsync(new SceneLoadRequest("Missing"));

            Assert.IsTrue(opening.IsDone);
            SceneLoadResult result = opening.GetResult();
            Assert.AreEqual(SceneLoadStatus.InvalidScene, result.Status);
            Assert.AreEqual(SceneLoadStage.Validate, result.Stage);
            Assert.AreEqual(0, m_backend.LoadCalls);
            Assert.IsFalse(m_service.IsBusy);

            yield return null;
        }

        [UnityTest]
        public IEnumerator LoadAsync_BackendThrows_ReturnsFailedAndReleasesBusy()
        {
            m_backend.ThrowOnLoad = true;

            var holder = new ResultHolder();
            yield return Load(new SceneLoadRequest("Broken"), holder);

            Assert.AreEqual(SceneLoadStatus.Failed, holder.Result.Status);
            Assert.AreEqual(SceneLoadStage.SceneLoad, holder.Result.Stage);
            Assert.IsNotNull(holder.Result.Error);
            Assert.IsFalse(m_service.IsBusy);
            Assert.AreEqual(0, m_service.LoadedContexts.Count);
        }

        [UnityTest]
        public IEnumerator LoadAsync_WithInitializer_OnlyReadyAfterItCompletes()
        {
            var initializer = new ManualInitializer();
            IAwaitable<SceneLoadResult> opening =
                m_service.LoadAsync(new SceneLoadRequest("WithInit").WithInitializer(initializer));

            while (initializer.Calls == 0)
            {
                yield return null;
            }

            Assert.IsTrue(m_service.IsBusy, "初始化器未完成前不能释放通道");
            Assert.AreEqual(SceneLoadState.Loaded, m_service.LoadedContexts[0].State, "Unity 已加载，但尚未 Ready");
            Assert.IsFalse(m_service.LoadedContexts[0].IsReady);

            initializer.Complete();

            while (!opening.IsDone)
            {
                yield return null;
            }

            SceneLoadResult result = opening.GetResult();
            Assert.AreEqual(SceneLoadStatus.Success, result.Status);
            Assert.AreEqual(SceneLoadState.Ready, result.Context.State);
            Assert.IsFalse(m_service.IsBusy);
        }

        [UnityTest]
        public IEnumerator LoadAsync_InitializerFails_ReportsFailedReadyButKeepsScene()
        {
            var initializer = new ManualInitializer();
            IAwaitable<SceneLoadResult> opening =
                m_service.LoadAsync(new SceneLoadRequest("InitFails").WithInitializer(initializer));

            while (initializer.Calls == 0)
            {
                yield return null;
            }

            initializer.Fail(new InvalidOperationException("init boom"));

            while (!opening.IsDone)
            {
                yield return null;
            }

            SceneLoadResult result = opening.GetResult();
            Assert.AreEqual(SceneLoadStatus.Failed, result.Status);
            Assert.AreEqual(SceneLoadStage.Ready, result.Stage);
            Assert.IsNotNull(result.Context);
            Assert.AreEqual(SceneLoadState.Failed, result.Context.State);
            Assert.AreEqual(1, m_service.LoadedContexts.Count, "初始化器失败不自动回滚场景");
            Assert.IsFalse(m_service.IsBusy);
        }

        [UnityTest]
        public IEnumerator LoadAsync_InitializerTimeout_KeepsBusyUntilTaskEnds()
        {
            m_service = new SceneService(0.05f, m_backend);
            var initializer = new ManualInitializer();

            IAwaitable<SceneLoadResult> opening =
                m_service.LoadAsync(new SceneLoadRequest("Timeout").WithInitializer(initializer));

            while (initializer.Calls == 0)
            {
                yield return null;
            }

            yield return new WaitForSeconds(0.2f);

            Assert.IsTrue(initializer.CancellationRequested, "超时必须请求协作取消");
            Assert.IsTrue(m_service.IsBusy, "旧任务真正结束前不能提前释放场景通道");

            initializer.Complete();

            while (!opening.IsDone)
            {
                yield return null;
            }

            SceneLoadResult result = opening.GetResult();
            Assert.AreEqual(SceneLoadStatus.Failed, result.Status);
            Assert.AreEqual(SceneLoadStage.Ready, result.Stage);
            Assert.IsInstanceOf<SceneInitializerTimeoutException>(result.Error);
            Assert.IsFalse(m_service.IsBusy);
        }

        [UnityTest]
        public IEnumerator UnloadAsync_LastScene_IsRejected()
        {
            var holder = new ResultHolder();
            yield return Load(new SceneLoadRequest("OnlyOne"), holder);

            IAwaitable<SceneLoadResult> unloading = m_service.UnloadAsync(holder.Result.Context);
            while (!unloading.IsDone)
            {
                yield return null;
            }

            SceneLoadResult result = unloading.GetResult();
            Assert.AreEqual(SceneLoadStatus.Failed, result.Status);
            Assert.AreEqual(SceneLoadStage.Unload, result.Stage);
            Assert.IsTrue(holder.Result.Context.IsAlive);
            Assert.AreEqual(1, m_service.LoadedContexts.Count);
        }

        [UnityTest]
        public IEnumerator UnloadAsync_ReleasesResourcesAndSwitchesActiveContext()
        {
            var first = new ResultHolder();
            yield return Load(new SceneLoadRequest("LevelA"), first);

            var second = new ResultHolder();
            yield return Load(new SceneLoadRequest("LevelB", LoadSceneMode.Additive), second);

            var disposable = new FakeDisposable();
            first.Result.Context.Resources.Track(disposable);
            Assert.AreEqual(1, first.Result.Context.Resources.Count);

            IAwaitable<SceneLoadResult> unloading = m_service.UnloadAsync(first.Result.Context);
            while (!unloading.IsDone)
            {
                yield return null;
            }

            Assert.AreEqual(SceneLoadStatus.Success, unloading.GetResult().Status);
            Assert.IsTrue(disposable.Disposed, "卸载场景必须释放其资源句柄");
            Assert.AreEqual(SceneLoadState.Unloaded, first.Result.Context.State);
            Assert.AreEqual(1, m_service.LoadedContexts.Count);
            Assert.AreSame(second.Result.Context, m_service.ActiveContext);

            // 迟到结果：范围已结束，Track 应立即释放
            var late = new FakeDisposable();
            first.Result.Context.Resources.Track(late);
            Assert.IsTrue(late.Disposed);
        }

        private IEnumerator Load(SceneLoadRequest request, ResultHolder holder)
        {
            IAwaitable<SceneLoadResult> opening = m_service.LoadAsync(request);
            while (!opening.IsDone)
            {
                yield return null;
            }

            holder.Result = opening.GetResult();
        }

        private sealed class ResultHolder
        {
            public SceneLoadResult Result;
        }

        /// <summary>可控完成时机、可注入失败的场景后端；用运行时空场景模拟真实 Scene 句柄。</summary>
        private sealed class FakeSceneBackend : ISceneBackend
        {
            private readonly Dictionary<string, Scene> m_scenes = new Dictionary<string, Scene>(StringComparer.Ordinal);
            private readonly List<FakeOperation> m_pending = new List<FakeOperation>();
            private static int s_counter;

            public bool CanLoadResult = true;
            public bool ThrowOnLoad;
            public bool AutoComplete = true;
            public int LoadCalls;

            public int PendingCount
            {
                get { return m_pending.Count; }
            }

            public bool CanLoad(string sceneNameOrPath)
            {
                return CanLoadResult && !string.IsNullOrEmpty(sceneNameOrPath);
            }

            public ISceneOperation LoadScene(string sceneNameOrPath, LoadSceneMode mode)
            {
                LoadCalls++;

                if (ThrowOnLoad)
                {
                    throw new InvalidOperationException("fake load failure");
                }

                Scene scene = SceneManager.CreateScene(string.Format("{0}_{1}", sceneNameOrPath, ++s_counter));
                m_scenes[sceneNameOrPath] = scene;

                var operation = new FakeOperation
                {
                    IsDone = AutoComplete,
                    Progress = AutoComplete ? 1f : 0f
                };

                if (!AutoComplete)
                {
                    m_pending.Add(operation);
                }

                return operation;
            }

            public void CompletePending()
            {
                for (int i = 0; i < m_pending.Count; i++)
                {
                    m_pending[i].Progress = 1f;
                    m_pending[i].IsDone = true;
                }

                m_pending.Clear();
            }

            public ISceneOperation UnloadScene(Scene scene)
            {
                string key = FindKey(scene);
                if (key != null)
                {
                    m_scenes.Remove(key);
                }

                if (!scene.IsValid() || !scene.isLoaded)
                {
                    return CompletedOperation.Instance;
                }

                AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
                return operation == null
                    ? (ISceneOperation)CompletedOperation.Instance
                    : new AsyncOperationWrapper(operation);
            }

            public Scene FindLoadedScene(string sceneNameOrPath)
            {
                Scene scene;
                return m_scenes.TryGetValue(sceneNameOrPath, out scene) ? scene : default(Scene);
            }

            public bool SetActiveScene(Scene scene)
            {
                // 测试中不真正切换活动场景，避免卸载活动场景的限制
                return scene.IsValid();
            }

            public int LoadedSceneCount
            {
                get { return m_scenes.Count; }
            }

            public IEnumerator UnloadAll()
            {
                var scenes = new List<Scene>(m_scenes.Values);
                m_scenes.Clear();

                for (int i = 0; i < scenes.Count; i++)
                {
                    if (scenes[i].IsValid() && scenes[i].isLoaded)
                    {
                        SceneManager.UnloadSceneAsync(scenes[i]);
                    }
                }

                // 等待卸载完成，避免残留场景影响后续用例
                for (int frame = 0; frame < 10; frame++)
                {
                    bool anyLoaded = false;
                    for (int i = 0; i < scenes.Count; i++)
                    {
                        if (scenes[i].IsValid() && scenes[i].isLoaded)
                        {
                            anyLoaded = true;
                            break;
                        }
                    }

                    if (!anyLoaded)
                    {
                        yield break;
                    }

                    yield return null;
                }
            }

            private string FindKey(Scene scene)
            {
                foreach (KeyValuePair<string, Scene> pair in m_scenes)
                {
                    if (pair.Value == scene)
                    {
                        return pair.Key;
                    }
                }

                return null;
            }
        }

        private sealed class FakeOperation : ISceneOperation
        {
            public bool IsDone { get; set; }

            public float Progress { get; set; }
        }

        private sealed class AsyncOperationWrapper : ISceneOperation
        {
            private readonly AsyncOperation m_operation;

            public AsyncOperationWrapper(AsyncOperation operation)
            {
                m_operation = operation;
            }

            public bool IsDone
            {
                get { return m_operation == null || m_operation.isDone; }
            }

            public float Progress
            {
                get { return m_operation == null ? 1f : m_operation.progress; }
            }
        }

        private sealed class CompletedOperation : ISceneOperation
        {
            public static readonly CompletedOperation Instance = new CompletedOperation();

            public bool IsDone
            {
                get { return true; }
            }

            public float Progress
            {
                get { return 1f; }
            }
        }

        private sealed class ManualInitializer : ISceneInitializer
        {
            private readonly Yato.Resources.AwaitableCompletionSource m_source =
                new Yato.Resources.AwaitableCompletionSource();

            public int Calls;
            public bool CancellationRequested;

            public IAwaitable InitializeAsync(SceneContext context, CancellationToken cancellationToken)
            {
                Calls++;

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(delegate { CancellationRequested = true; });
                }

                return m_source;
            }

            public void Complete()
            {
                m_source.SetResult();
            }

            public void Fail(Exception error)
            {
                m_source.SetException(error);
            }
        }

        private sealed class FakeDisposable : IDisposable
        {
            public bool Disposed;

            public void Dispose()
            {
                Disposed = true;
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
