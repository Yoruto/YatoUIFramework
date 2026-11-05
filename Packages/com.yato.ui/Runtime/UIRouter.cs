using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using PureMVC.Interfaces;
using UnityEngine;
using Yato.Core;
using Yato.Resources;

namespace Yato.UI
{
    /// <summary>
    /// 页面路由（取代 UIManager + CreateUICommand）。
    /// 打开时序：加载 → 实例化 → 创建 Mediator/Proxy → 注册 → OnCreate → 入栈(旧页 OnCover)
    /// → OnOpen(args) → 激活 GameObject（最后一步）。
    /// </summary>
    public static partial class UIRouter
    {
        private static IResourceService s_resources = NullResourceService.Instance;
        private static IFacade s_facade;
        private static UIPageStack s_stack = new UIPageStack();
        private static UIPageCache s_cache = new UIPageCache();
        private static UIPageFactory s_factory;

        /// <summary>打开中的请求：PageId → 请求（T-P3-01 取消支持）。</summary>
        private static readonly Dictionary<string, PendingOpen> s_pendingOpens =
            new Dictionary<string, PendingOpen>(StringComparer.Ordinal);

        /// <summary>非单例页面的请求序号：避免并发请求互相覆盖。</summary>
        private static int s_pendingSequence;
        private static long s_generation;
        private static bool s_reconfiguring;

        /// <summary>加载遮罩出现阈值（秒）。</summary>
        public static float LoadingThresholdSeconds = 0.15f;

        private static int s_loadingWatchCount;
        private static bool s_loadingVisible;

        /// <summary>当前资源服务。</summary>
        public static IResourceService Resources
        {
            get { return s_resources; }
        }

        /// <summary>当前门面（可为空，为空时跳过 Mediator/Proxy 注册）。</summary>
        public static IFacade Facade
        {
            get { return s_facade; }
        }

        /// <summary>页面栈（诊断 / 测试用）。</summary>
        public static UIPageStack Stack
        {
            get { return StackInstance; }
        }

        /// <summary>缓存容量，默认 8。缓存非空时修改会被忽略，避免实例泄漏。</summary>
        public static int CacheCapacity
        {
            get { return CacheInstance.Capacity; }
            set
            {
                if (CacheInstance.Count > 0)
                {
                    Log.Warn("[Yato.UI] 缓存非空，忽略容量调整；请先 CloseAll 再设置");
                    return;
                }

                s_cache = new UIPageCache(value);
            }
        }

        /// <summary>注入资源服务与门面，并取消尚未完成的打开请求。已打开页面保持原生命周期。</summary>
        public static void Configure(IResourceService resources, IFacade facade = null)
        {
            EnsureDiagnostics();
            bool wasReconfiguring = s_reconfiguring;
            s_reconfiguring = true;
            try
            {
                InvalidatePendingOpens();
                s_resources = resources ?? NullResourceService.Instance;
                s_facade = facade;
                s_factory = new UIPageFactory(s_resources);
            }
            finally { s_reconfiguring = wasReconfiguring; }
            Log.Info(string.Format(
                "[Yato.UI] UIRouter 已配置：resources={0} facade={1}",
                s_resources.GetType().Name,
                s_facade != null ? s_facade.GetType().Name : "none"));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayMode()
        {
            bool wasReconfiguring = s_reconfiguring;
            s_reconfiguring = true;
            try
            {
                InvalidatePendingOpens();
                s_diagnosticRegistration?.Dispose(); s_diagnosticRegistration = null;
                ResetUiExtensions();
                // 丢弃前必须释放实例与句柄，否则关闭 Domain Reload 时缓存页会跨会话泄漏
                DisposeAllPages();

                s_resources = NullResourceService.Instance;
                s_facade = null;
                s_stack = new UIPageStack();
                s_cache = new UIPageCache();
                s_factory = null;
                s_pendingSequence = 0;
                s_loadingWatchCount = 0;
                s_loadingVisible = false;
            }
            finally { s_reconfiguring = wasReconfiguring; }
        }

        private static void InvalidatePendingOpens()
        {
            s_generation++;
            var pending = new List<PendingOpen>(s_pendingOpens.Values);
            s_pendingOpens.Clear();
            s_loadingWatchCount = 0;
            if (s_loadingVisible) UILoading.Hide();
            s_loadingVisible = false;
            foreach (var opening in pending) opening.Cancel();
        }

        /// <summary>销毁栈与缓存中的全部页面上下文（不发送通知）。</summary>
        private static void DisposeAllPages()
        {
            if (s_cache != null)
            {
                List<UIPageContext> cached = s_cache.Clear();
                for (int i = 0; i < cached.Count; i++)
                {
                    DestroyContext(cached[i]);
                }
            }

            if (s_stack == null)
            {
                return;
            }

            List<UIPageHandle> handles = s_stack.GetAllHandles();
            for (int i = 0; i < handles.Count; i++)
            {
                UIPageHandle handle = handles[i];
                if (handle == null)
                {
                    continue;
                }

                handle.IsValid = false;
                DestroyContext(handle.Context);
            }

            s_stack.Clear();
        }

        /// <summary>异步打开页面。</summary>
        public static IAwaitable<UIPageHandle> OpenAsync(UIPageDef def, object args = null, UILayer? layer = null)
        {
            // Unity 6 也有 UnityEngine.AwaitableCompletionSource<T>，此处必须全限定
            var source = new Yato.Resources.AwaitableCompletionSource<UIPageHandle>();

            if (s_reconfiguring)
            {
                source.SetException(new OperationCanceledException("UIRouter is being reset or configured"));
                return source;
            }

            if (def == null || !def.IsValid)
            {
                source.SetException(new ResourceException(
                    null,
                    null,
                    ResourceStage.ProviderSelect,
                    string.Format("UIPageDef 非法：{0}", def == null ? "null" : def.PageId)));
                return source;
            }

            UILayer targetLayer = layer ?? def.Layer;

            // 单例页面已在栈上：直接复用，不产生孤儿实例（DEF-05）
            if (def.Singleton)
            {
                UIPageHandle opened = StackInstance.Find(def.PageId);
                if (opened != null && opened.Context != null)
                {
                    Reveal(opened.Context, args);
                    source.SetResult(opened);
                    return source;
                }
            }

            // 同一页面已有进行中的请求：单飞，后续调用共享同一个结果，
            // 避免"加载过程中再次 Open"产生两个实例同时入栈（C1）。
            PendingOpen running;
            if (def.Singleton && s_pendingOpens.TryGetValue(def.PageId, out running))
            {
                return running.Result;
            }

            // 缓存命中：复用实例，不再走资源加载（仅单例页面参与缓存）
            UIPageContext cached;
            if (def.Singleton && CacheInstance.TryGet(def.PageId, out cached))
            {
                // 取出的实例回到页面栈，不能同时留在缓存里（否则重复关闭会覆盖并泄漏）
                CacheInstance.Remove(def.PageId, out cached);
                source.SetResult(RestoreFromCache(cached, args, targetLayer));
                return source;
            }

            string pendingKey = def.Singleton
                ? def.PageId
                : string.Format("{0}#{1}", def.PageId, Interlocked.Increment(ref s_pendingSequence));

            var pending = new PendingOpen(pendingKey, def, args, targetLayer, s_generation);
            s_pendingOpens[pendingKey] = pending;
            BeginLoadingWatch();

            IAwaitable<UIPageContext> creation;
            try
            {
                creation = FactoryInstance.CreateForRouterAsync(def, args, targetLayer, null, pending.Token);
            }
            catch (Exception e)
            {
                // 工厂同步失败（如非法层级）：必须清理进行中状态与加载计数
                RemovePending(pending);
                EndLoadingWatch(pending);
                Log.Error("UIRouter", string.Format("打开页面失败：{0}", def.PageId), e);
                Notify(FrameworkNotifications.UIPageFailed, e);
                pending.Fail(e);
                return pending.Result;
            }

            creation.Completed += delegate
            {
                UIPageContext context = null;
                try
                {
                    context = creation.GetResult();

                    if (pending.IsCancelled || pending.Generation != s_generation)
                    {
                        // 打开过程中被取消：销毁已创建实例，不回调、不产生孤儿（T-P3-01）
                        DestroyContext(context);
                        pending.Fail(new OperationCanceledException());
                        return;
                    }

                    UIPageHandle handle = Activate(context, args, targetLayer, pending);
                    context = null; // 所有权已移交给页面栈
                    pending.Complete(handle);
                    RefreshMask();
                }
                catch (Exception e)
                {
                    // 激活阶段失败（如非法层级）：已创建实例与预制体句柄必须回收
                    if (context != null)
                    {
                        DestroyContext(context);
                    }

                    if (!(e is OperationCanceledException) && pending.Generation == s_generation)
                    {
                        Log.Error("UIRouter", string.Format("打开页面失败：{0}", def.PageId), e);
                        Notify(FrameworkNotifications.UIPageFailed, e);
                    }
                    pending.Fail(e);
                }
                finally
                {
                    RemovePending(pending);
                    EndLoadingWatch(pending);
                }
            };

            return pending.Result;
        }

        /// <summary>关闭页面（幂等）。</summary>
        public static void Close(UIPageHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            UIPageContext context = handle.Context;
            if (context == null || !handle.IsValid)
            {
                return;
            }

            // 过期句柄：该 context 已被新句柄接管时，关闭旧句柄不能销毁仍在使用的实例
            if (context.Handle != null && !ReferenceEquals(context.Handle, handle))
            {
                Log.Warn(string.Format("[Yato.UI] 忽略过期句柄：{0}", context.PageId));
                handle.IsValid = false;
                return;
            }

            if (context.Instance != null) context.Instance.GetComponent<UIPageTransition>()?.Cancel();
            handle.IsValid = false;

            bool cacheable = context.Def != null && context.Def.Cacheable && context.Def.Singleton;

            // 顺序：OnClose（此时 Mediator/Proxy 尚未注销，仍可发通知）→ 出栈 → 注销 → 销毁/缓存
            SafePage(context, delegate(IUIPage page) { page.OnClose(!cacheable); });

            StackInstance.Pop(handle);
            context.IsOpen = false;
            Unregister(context);

            if (cacheable)
            {
                if (context.Instance != null)
                {
                    context.Instance.SetActive(false);
                }

                UIPageContext evicted = CacheInstance.Add(context.PageId, context);
                if (evicted != null)
                {
                    DestroyContext(evicted);
                }
            }
            else
            {
                DestroyContext(context);
            }

            Notify(FrameworkNotifications.UIPageClosed, context.PageId);
            RaisePage(PageClosed, handle);
            PublishTop();
            RefreshMask();
        }

        /// <summary>返回键：关闭最上层的可返回页面。返回是否有关闭行为。</summary>
        public static bool Back()
        {
            UIPageHandle top = StackInstance.Back();
            if (top == null)
            {
                return false;
            }

            var focus = top.Context.Instance != null ? top.Context.Instance.GetComponent<UIPageFocus>() : null;
            if (focus != null && !focus.AllowBack) return false;
            Close(top);
            return true;
        }

        /// <summary>页面是否已打开（含缓存实例）。</summary>
        public static bool IsOpen(string pageId)
        {
            if (string.IsNullOrEmpty(pageId))
            {
                return false;
            }

            return StackInstance.Contains(pageId) || CacheInstance.Contains(pageId);
        }

        /// <summary>关闭全部页面；指定层级时只关闭该层。</summary>
        public static void CloseAll(UILayer? layer = null)
        {
            List<UIPageHandle> handles = layer.HasValue
                ? StackInstance.GetHandles(layer.Value)
                : StackInstance.GetAllHandles();

            for (int i = handles.Count - 1; i >= 0; i--)
            {
                Close(handles[i]);
            }

            if (!layer.HasValue)
            {
                List<UIPageContext> cached = CacheInstance.Clear();
                for (int i = 0; i < cached.Count; i++)
                {
                    DestroyContext(cached[i]);
                }

                return;
            }

            // 按层关闭时，该层缓存实例也必须一并清理，否则 IsOpen 检查仍会命中
            CloseCachedOnLayer(layer.Value);
        }

        /// <summary>销毁指定层级的缓存实例。</summary>
        private static void CloseCachedOnLayer(UILayer layer)
        {
            List<string> pageIds = CacheInstance.GetPageIds();
            for (int i = 0; i < pageIds.Count; i++)
            {
                UIPageContext context;
                if (!CacheInstance.TryGet(pageIds[i], out context) || context == null || context.Layer != layer)
                {
                    continue;
                }

                UIPageContext removed;
                if (CacheInstance.Remove(pageIds[i], out removed))
                {
                    DestroyContext(removed);
                }
            }
        }

        /// <summary>取消「打开中」的页面（尚未完成加载）。返回是否命中（T-P3-01）。</summary>
        public static bool CancelOpen(string pageId)
        {
            if (string.IsNullOrEmpty(pageId))
            {
                return false;
            }

            PendingOpen pending = null;
            if (!s_pendingOpens.TryGetValue(pageId, out pending))
            {
                // 非单例页面的 pending key 带实例序号，按 PageId 回退匹配
                foreach (KeyValuePair<string, PendingOpen> pair in s_pendingOpens)
                {
                    if (pair.Value != null && pair.Value.Def != null && pair.Value.Def.PageId == pageId)
                    {
                        pending = pair.Value;
                        break;
                    }
                }
            }

            if (pending == null)
            {
                return false;
            }

            RemovePending(pending);
            EndLoadingWatch(pending);
            pending.Cancel();
            Log.Info(string.Format("[Yato.UI] 已取消打开：{0}", pageId));
            return true;
        }

        /// <summary>页面是否在缓存中（LRU，可直接复用）。</summary>
        public static bool IsCached(string pageId)
        {
            return !string.IsNullOrEmpty(pageId) && CacheInstance.Contains(pageId);
        }

        /// <summary>预热页面预制体，二次打开不再等待 IO（T-P3-03）。</summary>
        public static IAwaitable PreloadAsync(params UIPageDef[] defs)
        {
            var keys = new List<AssetKey>();
            if (defs != null)
            {
                for (int i = 0; i < defs.Length; i++)
                {
                    UIPageDef def = defs[i];
                    if (def != null && def.PrefabKey.IsValid)
                    {
                        keys.Add(def.PrefabKey);
                    }
                }
            }

            IResourceService resources = s_resources ?? NullResourceService.Instance;
            return resources.PreloadAsync(keys);
        }

        /// <summary>有弹窗时显示遮罩，最后一个弹窗关闭后隐藏（T-P3-06）。</summary>
        private static void RefreshMask()
        {
            UIPageHandle top = StackInstance.Peek(UILayer.Popup);
            if (top != null)
            {
                UIMask.Show(OnMaskClicked);
            }
            else
            {
                UIMask.Hide();
            }
        }

        private static void OnMaskClicked()
        {
            UIPageHandle top = StackInstance.Peek(UILayer.Popup);
            if (top != null)
            {
                Close(top);
            }
        }

        private static void BeginLoadingWatch()
        {
            s_loadingWatchCount++;
            RuntimeHost.RunCoroutine(ShowLoadingAfterThreshold(s_generation));
        }

        private static IEnumerator ShowLoadingAfterThreshold(long generation)
        {
            float elapsed = 0f;
            while (elapsed < LoadingThresholdSeconds)
            {
                if (generation != s_generation) yield break;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (generation == s_generation && s_loadingWatchCount > 0 && !s_loadingVisible)
            {
                s_loadingVisible = true;
                UILoading.Show("加载中...");
            }
        }

        private static void EndLoadingWatch(PendingOpen pending)
        {
            if (!pending.LoadingWatchActive) return;
            pending.LoadingWatchActive = false;
            if (pending.Generation != s_generation) return;
            if (s_loadingWatchCount > 0)
            {
                s_loadingWatchCount--;
            }

            if (s_loadingWatchCount == 0 && s_loadingVisible)
            {
                s_loadingVisible = false;
                UILoading.Hide();
            }
        }

        /// <summary>一次进行中的打开请求：同一页面并发打开时共享同一个结果。</summary>
        private sealed class PendingOpen
        {
            private readonly CancellationTokenSource m_source = new CancellationTokenSource();
            private readonly Yato.Resources.AwaitableCompletionSource<UIPageHandle> m_result =
                new Yato.Resources.AwaitableCompletionSource<UIPageHandle>();

            private bool m_cancelled;
            private bool m_cancelling;
            private bool m_disposed;

            public PendingOpen(string key, UIPageDef def, object args, UILayer layer, long generation)
            {
                Key = key;
                Def = def;
                Args = args;
                Layer = layer;
                Generation = generation;
                Token = m_source.Token;
            }

            public string Key { get; private set; }

            public UIPageDef Def { get; private set; }

            public object Args { get; private set; }

            public UILayer Layer { get; private set; }

            /// <summary>所有调用方共享的打开结果。</summary>
            public IAwaitable<UIPageHandle> Result
            {
                get { return m_result; }
            }

            public long Generation { get; private set; }
            public bool LoadingWatchActive = true;
            public CancellationToken Token { get; private set; }

            public bool IsCancelled
            {
                get { return m_cancelled; }
            }

            public void Cancel()
            {
                m_cancelled = true;
                m_cancelling = true;
                try
                {
                    m_source.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception error) { Log.Error("UIRouter", "Open cancellation observer failed", error); }
                finally
                {
                    m_cancelling = false;
                    Fail(new OperationCanceledException("UI page opening was cancelled"));
                }
            }

            public void Complete(UIPageHandle handle)
            {
                m_result.SetResult(handle);
                DisposeTokenSource();
            }

            public void Fail(Exception error)
            {
                m_result.SetException(error);
                DisposeTokenSource();
            }

            private void DisposeTokenSource()
            {
                if (m_disposed || m_cancelling) return;
                m_disposed = true;
                m_source.Dispose();
            }
        }

        /// <summary>移除登记；只移除自己，避免误删同 PageId 的新请求。</summary>
        private static void RemovePending(PendingOpen pending)
        {
            if (pending == null)
            {
                return;
            }

            PendingOpen registered;
            if (s_pendingOpens.TryGetValue(pending.Key, out registered) && ReferenceEquals(registered, pending))
            {
                s_pendingOpens.Remove(pending.Key);
            }
        }

        #region 内部实现

        private static UIPageStack StackInstance
        {
            get
            {
                if (s_stack == null)
                {
                    s_stack = new UIPageStack();
                }

                return s_stack;
            }
        }

        private static UIPageCache CacheInstance
        {
            get
            {
                if (s_cache == null)
                {
                    s_cache = new UIPageCache();
                }

                return s_cache;
            }
        }

        private static UIPageFactory FactoryInstance
        {
            get
            {
                if (s_factory == null)
                {
                    s_factory = new UIPageFactory(s_resources ?? NullResourceService.Instance);
                }

                return s_factory;
            }
        }

        /// <summary>首次显示：注册 → 入栈（旧页 OnCover）→ OnOpen → 激活。</summary>
        private static UIPageHandle Activate(UIPageContext context, object args, UILayer layer, PendingOpen pending)
        {
            context.Layer = layer;
            context.Args = args;

            if (context.Instance != null)
            {
                context.Instance.transform.SetParent(UIRoot.Instance.GetContainer(layer), false);
            }

            var handle = new UIPageHandle(context) { IsValid = true };
            context.Handle = handle;

            Register(context);
            try
            {
                EnsureCurrentOpening(pending, context);
                context.Page.OnCreate();
                EnsureCurrentOpening(pending, context);
                StackInstance.Push(handle);
                context.IsOpen = true;
                EnsureCurrentOpening(pending, context);

                SafePage(context, delegate(IUIPage page) { page.OnOpen(args); });
                EnsureCurrentOpening(pending, context);

                if (context.Instance != null)
                {
                    context.Instance.SetActive(true);
                }

                Notify(FrameworkNotifications.UIPageOpened, handle);
                RaisePage(PageOpened, handle);
                PublishTop();
                EnsureCurrentOpening(pending, context);
                return handle;
            }
            catch
            {
                handle.IsValid = false;
                if (context.IsOpen)
                {
                    SafePage(context, page => page.OnClose(true));
                    StackInstance.Pop(handle);
                    context.IsOpen = false;
                }
                Unregister(context);
                throw;
            }
        }

        private static void EnsureCurrentOpening(PendingOpen pending, UIPageContext context)
        {
            if (pending.IsCancelled || pending.Generation != s_generation || context.Handle == null ||
                !context.Handle.IsValid || context.Instance == null)
                throw new OperationCanceledException("UIRouter changed or the page closed while opening");
        }

        /// <summary>单例页面重复打开：只重新播报 OnOpen 并激活。</summary>
        private static void Reveal(UIPageContext context, object args)
        {
            if (context.Instance != null) context.Instance.GetComponent<UIPageTransition>()?.Cancel();
            context.Args = args;
            if (context.Instance != null)
            {
                context.Instance.SetActive(true);
            }

            SafePage(context, delegate(IUIPage page) { page.OnOpen(args); });
        }

        private static UIPageHandle RestoreFromCache(UIPageContext context, object args, UILayer layer)
        {
            context.Layer = layer;
            context.Args = args;

            var handle = new UIPageHandle(context) { IsValid = true };
            context.Handle = handle;

            if (context.Instance != null)
            {
                context.Instance.transform.SetParent(UIRoot.Instance.GetContainer(layer), false);
                context.Instance.SetActive(false);
            }

            Register(context);

            StackInstance.Push(handle);
            context.IsOpen = true;

            SafePage(context, delegate(IUIPage page) { page.OnOpen(args); });

            if (context.Instance != null)
            {
                context.Instance.SetActive(true);
            }

            Notify(FrameworkNotifications.UIPageOpened, handle);
            RaisePage(PageOpened, handle);
            PublishTop();
            return handle;
        }

        private static void Register(UIPageContext context)
        {
            var facade = s_facade;
            if (facade == null)
            {
                return;
            }

            try
            {
                context.RegisteredFacade = facade;
                if (context.Proxy != null)
                {
                    facade.RegisterProxy(context.Proxy);
                }

                if (context.Mediator != null)
                {
                    facade.RegisterMediator(context.Mediator);
                }
            }
            catch (Exception e)
            {
                Log.Error("UIRouter", string.Format("注册页面失败：{0}", context.PageId), e);
            }
        }

        private static void Unregister(UIPageContext context)
        {
            var facade = context.RegisteredFacade;
            context.RegisteredFacade = null;
            if (facade == null)
            {
                return;
            }

            try
            {
                if (context.Mediator != null && ReferenceEquals(facade.RetrieveMediator(context.Mediator.MediatorName), context.Mediator))
                {
                    facade.RemoveMediator(context.Mediator.MediatorName);
                }

                if (context.Proxy != null && ReferenceEquals(facade.RetrieveProxy(context.Proxy.ProxyName), context.Proxy))
                {
                    facade.RemoveProxy(context.Proxy.ProxyName);
                }
            }
            catch (Exception e)
            {
                Log.Error("UIRouter", string.Format("注销页面失败：{0}", context.PageId), e);
            }
        }

        private static void DestroyContext(UIPageContext context)
        {
            if (context == null)
            {
                return;
            }

            if (context.IsOpen)
            {
                context.IsOpen = false;
                SafePage(context, page => page.OnClose(true));
            }
            if (context.Handle != null) context.Handle.IsValid = false;
            Unregister(context);

            if (context.Instance != null)
            {
                UnityEngine.Object.Destroy(context.Instance);
                context.Instance = null;
            }

            if (context.PrefabHandle != null)
            {
                context.PrefabHandle.Dispose();
                context.PrefabHandle = null;
            }

            context.IsOpen = false;
            context.Handle = null;
        }

        private static void SafePage(UIPageContext context, Action<IUIPage> action)
        {
            if (context == null || context.Page == null || action == null)
            {
                return;
            }

            try
            {
                action(context.Page);
            }
            catch (Exception e)
            {
                Log.Error("UIRouter", string.Format("页面回调异常：{0}", context.PageId), e);
            }
        }

        private static void Notify(string notificationName, object body)
        {
            if (s_facade == null || string.IsNullOrEmpty(notificationName))
            {
                return;
            }

            try
            {
                s_facade.SendNotification(notificationName, body);
            }
            catch (Exception e)
            {
                Log.Error("UIRouter", string.Format("发送通知失败：{0}", notificationName), e);
            }
        }

        #endregion
    }
}
