using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yato.Core;
using Yato.Resources;

namespace Yato.App
{
    /// <summary>
    /// 场景服务实现（D03）。
    /// <para>Single / Additive / Unload / Ready 共用一个互斥通道：忙时立即 Busy，不排队。</para>
    /// <para>取消只在 Unity 加载真正开始前生效；开始后不承诺中断 Unity 加载。</para>
    /// <para>Loaded 与 Ready 分离；初始化器超时后不提前释放通道，必须等旧任务真正结束。</para>
    /// </summary>
    public sealed class SceneService : ISceneService, IAppService
    {
        /// <summary>Ready 初始化器默认超时（秒）。</summary>
        public const float DefaultReadyTimeoutSeconds = 60f;

        private readonly List<SceneContext> m_contexts = new List<SceneContext>();
        private readonly ISceneBackend m_backend;
        private readonly float m_defaultReadyTimeout;

        private SceneContext m_active;
        private bool m_busy;
        private SceneLoadResult m_lastResult;

        public SceneService(float defaultReadyTimeoutSeconds = DefaultReadyTimeoutSeconds, ISceneBackend backend = null)
        {
            m_defaultReadyTimeout = defaultReadyTimeoutSeconds > 0f
                ? defaultReadyTimeoutSeconds
                : DefaultReadyTimeoutSeconds;
            m_backend = backend ?? new UnitySceneBackend();
        }

        /// <summary>场景后端（默认直接调用 SceneManager）。</summary>
        public ISceneBackend Backend
        {
            get { return m_backend; }
        }

        public string Name
        {
            get { return "SceneService"; }
        }

        public int Order
        {
            get { return -100; }
        }

        public bool IsBusy
        {
            get { return m_busy; }
        }

        public SceneContext ActiveContext
        {
            get { return m_active; }
        }

        public IReadOnlyList<SceneContext> LoadedContexts
        {
            get { return m_contexts; }
        }

        public SceneLoadResult LastResult
        {
            get { return m_lastResult; }
        }

        public event Action<SceneLoadRequest> LoadStarted;
        public event Action<SceneLoadResult> LoadCompleted;
        public event Action<SceneContext> ContextLoaded;
        public event Action<SceneContext> ContextReady;
        public event Action<SceneContext> ContextUnloaded;

        public void Initialize()
        {
        }

        public void Shutdown()
        {
            m_busy = false;

            for (int i = m_contexts.Count - 1; i >= 0; i--)
            {
                SceneContext context = m_contexts[i];
                context.MarkUnloaded();
                Raise(ContextUnloaded, context);
            }

            m_contexts.Clear();
            m_active = null;
        }

        public IAwaitable<SceneLoadResult> LoadAsync(SceneLoadRequest request)
        {
            var source = new Yato.Resources.AwaitableCompletionSource<SceneLoadResult>();

            if (request == null || string.IsNullOrEmpty(request.SceneName))
            {
                Reject(source, SceneLoadStatus.InvalidScene, SceneLoadStage.Validate, request, null, "场景名不能为空");
                return source;
            }

            if (m_busy)
            {
                Reject(source, SceneLoadStatus.Busy, SceneLoadStage.None, request, null,
                    string.Format("已有场景操作进行中，拒绝加载：{0}", request.SceneName));
                return source;
            }

            if (!m_backend.CanLoad(request.SceneName))
            {
                Reject(source, SceneLoadStatus.InvalidScene, SceneLoadStage.Validate, request, null,
                    string.Format("场景不可加载（默认实现要求加入 Build Settings）：{0}", request.SceneName));
                return source;
            }

            if (request.CancellationToken.IsCancellationRequested)
            {
                Reject(source, SceneLoadStatus.Canceled, SceneLoadStage.CancelCheck, request, null,
                    "调用方在开始前已取消");
                return source;
            }

            m_busy = true;
            Raise(LoadStarted, request);

            try
            {
                RuntimeHost.RunCoroutine(LoadRoutine(request, source));
            }
            catch (Exception e)
            {
                // 协程都没能启动（如宿主不可用）：释放通道并报告，不能把 busy 卡死
                Finish(source, SceneLoadStatus.Failed, SceneLoadStage.SceneLoad, request, null, e,
                    "启动场景加载失败");
            }

            return source;
        }

        public IAwaitable<SceneLoadResult> UnloadAsync(SceneContext context)
        {
            var source = new Yato.Resources.AwaitableCompletionSource<SceneLoadResult>();

            if (context == null)
            {
                Reject(source, SceneLoadStatus.InvalidScene, SceneLoadStage.Validate, null, null, "场景上下文为空");
                return source;
            }

            if (m_busy)
            {
                Reject(source, SceneLoadStatus.Busy, SceneLoadStage.None,
                    new SceneLoadRequest(context.SceneName, context.Mode), context,
                    string.Format("已有场景操作进行中，拒绝卸载：{0}", context.SceneName));
                return source;
            }

            if (!m_contexts.Contains(context))
            {
                Reject(source, SceneLoadStatus.InvalidScene, SceneLoadStage.Unload,
                    new SceneLoadRequest(context.SceneName, context.Mode), context,
                    "该场景上下文不在管理中（可能已卸载）");
                return source;
            }

            // D03：没有可用替代场景时禁止卸载最后一个场景
            if (m_backend.LoadedSceneCount <= 1)
            {
                Reject(source, SceneLoadStatus.Failed, SceneLoadStage.Unload,
                    new SceneLoadRequest(context.SceneName, context.Mode), context,
                    "不能卸载最后一个可用场景");
                return source;
            }

            m_busy = true;

            try
            {
                RuntimeHost.RunCoroutine(UnloadRoutine(context, source));
            }
            catch (Exception e)
            {
                Finish(source, SceneLoadStatus.Failed, SceneLoadStage.Unload,
                    new SceneLoadRequest(context.SceneName, context.Mode), context, e, "启动场景卸载失败");
            }

            return source;
        }

        private IEnumerator LoadRoutine(SceneLoadRequest request, Yato.Resources.AwaitableCompletionSource<SceneLoadResult> source)
        {
            // 1) 真正开始 Unity 加载之前留出一帧的取消窗口（D03：开始后不承诺取消）
            yield return null;

            if (request.CancellationToken.IsCancellationRequested)
            {
                Finish(source, SceneLoadStatus.Canceled, SceneLoadStage.CancelCheck, request, null, null,
                    "场景加载开始前被取消");
                yield break;
            }

            // 2) Unity 场景加载
            ISceneOperation operation = null;
            Exception failure = null;

            try
            {
                operation = m_backend.LoadScene(request.SceneName, request.Mode);
            }
            catch (Exception e)
            {
                failure = e;
            }

            if (failure == null && operation == null)
            {
                failure = new InvalidOperationException("场景后端未返回加载操作");
            }

            if (failure == null)
            {
                while (!operation.IsDone)
                {
                    Report(request, operation.Progress);
                    yield return null;
                }

                Report(request, 1f);
            }

            if (failure != null)
            {
                Finish(source, SceneLoadStatus.Failed, SceneLoadStage.SceneLoad, request, null, failure,
                    string.Format("场景加载失败：{0}", request.SceneName));
                yield break;
            }

            // 3) Loaded（不保证场景内 MonoBehaviour 的 Start 已执行）
            Scene scene;
            try
            {
                scene = m_backend.FindLoadedScene(request.SceneName);
            }
            catch (Exception e)
            {
                Finish(source, SceneLoadStatus.Failed, SceneLoadStage.Loaded, request, null, e,
                    "读取已加载场景失败");
                yield break;
            }

            if (!scene.IsValid())
            {
                Finish(source, SceneLoadStatus.Failed, SceneLoadStage.Loaded, request, null,
                    new InvalidOperationException("加载完成后找不到目标场景：" + request.SceneName),
                    "场景加载结果无效");
                yield break;
            }

            var context = new SceneContext(request.SceneName, request.Mode);
            context.MarkLoaded(scene);
            m_contexts.Add(context);

            if (request.Mode == LoadSceneMode.Single || m_active == null)
            {
                m_active = context;
            }

            if (request.ActivateOnLoad && request.Mode == LoadSceneMode.Single)
            {
                try
                {
                    m_backend.SetActiveScene(scene);
                }
                catch (Exception e)
                {
                    // 激活失败不视为加载失败，但必须可追踪
                    Log.Warn(string.Format("[Yato.App] 设置活动场景失败：{0}（{1}）", request.SceneName, e.Message));
                }
            }

            // Single 加载会销毁旧场景；同步清理已失效的上下文并释放其资源
            if (request.Mode == LoadSceneMode.Single)
            {
                PruneUnloadedContexts(context);
            }

            Raise(ContextLoaded, context);

            // 4) Ready（可选初始化器）
            InitializerOutcome outcome = null;
            if (request.Initializer != null)
            {
                outcome = new InitializerOutcome();
                float timeout = request.ReadyTimeoutSeconds > 0f
                    ? request.ReadyTimeoutSeconds
                    : m_defaultReadyTimeout;

                yield return RunInitializer(context, request.Initializer, timeout, outcome);

                if (outcome.Error != null)
                {
                    // D03：初始化器失败不自动回滚场景，保留 Loaded 状态并报告错误
                    context.MarkFailed(outcome.Error);
                    Finish(source, SceneLoadStatus.Failed, SceneLoadStage.Ready, request, context, outcome.Error,
                        outcome.TimedOut
                            ? string.Format("场景初始化器超时：{0}", context.SceneName)
                            : string.Format("场景初始化器失败：{0}", context.SceneName));
                    yield break;
                }
            }

            context.MarkReady();
            Raise(ContextReady, context);
            Finish(source, SceneLoadStatus.Success, SceneLoadStage.None, request, context, null, null);
        }

        private IEnumerator UnloadRoutine(SceneContext context, Yato.Resources.AwaitableCompletionSource<SceneLoadResult> source)
        {
            context.State = SceneLoadState.Unloading;

            // 先释放场景资源，再卸载场景；迟到的加载结果会因范围失效而立即回收
            context.Resources.ReleaseAll();

            ISceneOperation operation = null;
            Exception failure = null;

            try
            {
                operation = m_backend.UnloadScene(context.Scene);
            }
            catch (Exception e)
            {
                failure = e;
            }

            if (failure == null)
            {
                while (operation != null && !operation.IsDone)
                {
                    yield return null;
                }
            }

            var request = new SceneLoadRequest(context.SceneName, context.Mode);

            if (failure != null)
            {
                context.MarkFailed(failure);
                Finish(source, SceneLoadStatus.Failed, SceneLoadStage.Unload, request, context, failure,
                    string.Format("场景卸载失败：{0}", context.SceneName));
                yield break;
            }

            m_contexts.Remove(context);
            context.MarkUnloaded();

            if (ReferenceEquals(m_active, context))
            {
                m_active = m_contexts.Count > 0 ? m_contexts[m_contexts.Count - 1] : null;
            }

            Raise(ContextUnloaded, context);
            Finish(source, SceneLoadStatus.Success, SceneLoadStage.Unload, request, context, null, null);
        }

        /// <summary>
        /// 运行初始化器：超时后取消，但必须等任务真正结束才返回，
        /// 以免旧任务继续修改场景时释放通道导致并发操作（D03 的明确要求）。
        /// </summary>
        private IEnumerator RunInitializer(
            SceneContext context,
            ISceneInitializer initializer,
            float timeoutSeconds,
            InitializerOutcome outcome)
        {
            var cts = new CancellationTokenSource();
            IAwaitable task = null;

            try
            {
                task = initializer.InitializeAsync(context, cts.Token);
            }
            catch (Exception e)
            {
                outcome.Error = e;
            }

            if (outcome.Error == null && task == null)
            {
                outcome.Error = new InvalidOperationException("场景初始化器未返回可等待对象");
            }

            if (outcome.Error != null)
            {
                cts.Dispose();
                yield break;
            }

            float elapsed = 0f;
            while (!task.IsDone)
            {
                if (timeoutSeconds > 0f && elapsed >= timeoutSeconds)
                {
                    outcome.TimedOut = true;
                    try
                    {
                        cts.Cancel();
                    }
                    catch (Exception e)
                    {
                        Log.Warn(string.Format("[Yato.App] 取消场景初始化器失败：{0}（{1}）", context.SceneName, e.Message));
                    }

                    break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (outcome.TimedOut)
            {
                // 关键：不提前释放互斥通道，等待旧任务真正结束
                while (!task.IsDone)
                {
                    yield return null;
                }

                outcome.Error = new SceneInitializerTimeoutException(context.SceneName, timeoutSeconds);
                cts.Dispose();
                yield break;
            }

            try
            {
                task.GetResult();
            }
            catch (Exception e)
            {
                outcome.Error = e;
            }

            cts.Dispose();
        }

        /// <summary>移除已被 Unity 销毁（如 Single 加载）的上下文并释放其资源。</summary>
        private void PruneUnloadedContexts(SceneContext keep)
        {
            for (int i = m_contexts.Count - 1; i >= 0; i--)
            {
                SceneContext context = m_contexts[i];
                if (ReferenceEquals(context, keep))
                {
                    continue;
                }

                if (context.Scene.IsValid() && context.Scene.isLoaded)
                {
                    continue;
                }

                m_contexts.RemoveAt(i);
                context.MarkUnloaded();
                Raise(ContextUnloaded, context);
            }
        }

        private void Report(SceneLoadRequest request, float value)
        {
            if (request == null || request.Progress == null)
            {
                return;
            }

            try
            {
                request.Progress.Report(value);
            }
            catch (Exception)
            {
                // 进度回调属于尽力而为，异常不能影响场景加载
            }
        }

        /// <summary>拒绝请求：不占用/不释放互斥通道，也不触发 LoadStarted/LoadCompleted。</summary>
        private void Reject(
            Yato.Resources.AwaitableCompletionSource<SceneLoadResult> source,
            SceneLoadStatus status,
            SceneLoadStage stage,
            SceneLoadRequest request,
            SceneContext context,
            string message)
        {
            var result = SceneLoadResult.Create(status, stage, request, context, null, message);
            m_lastResult = result;
            source.SetResult(result);
        }

        /// <summary>结束一次已接受的操作：释放互斥通道并广播结果。</summary>
        private void Finish(
            Yato.Resources.AwaitableCompletionSource<SceneLoadResult> source,
            SceneLoadStatus status,
            SceneLoadStage stage,
            SceneLoadRequest request,
            SceneContext context,
            Exception error,
            string message)
        {
            m_busy = false;

            var result = SceneLoadResult.Create(status, stage, request, context, error, message);
            m_lastResult = result;

            if (LoadCompleted != null)
            {
                try
                {
                    LoadCompleted(result);
                }
                catch (Exception e)
                {
                    Log.Error("SceneService", "LoadCompleted 回调异常", e);
                }
            }

            source.SetResult(result);
        }

        private void Raise(Action<SceneLoadRequest> handlers, SceneLoadRequest request)
        {
            if (handlers == null)
            {
                return;
            }

            try
            {
                handlers(request);
            }
            catch (Exception e)
            {
                Log.Error("SceneService", "LoadStarted 回调异常", e);
            }
        }

        private void Raise(Action<SceneContext> handlers, SceneContext context)
        {
            if (handlers == null)
            {
                return;
            }

            try
            {
                handlers(context);
            }
            catch (Exception e)
            {
                Log.Error("SceneService", "场景事件回调异常", e);
            }
        }

        /// <summary>初始化器执行结果。</summary>
        private sealed class InitializerOutcome
        {
            public Exception Error;
            public bool TimedOut;
        }
    }
}
