using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Yato.Core;
using Yato.Resources;

namespace Yato.UI
{
    public static partial class UIRouter
    {
        public static event Action<UIPageHandle> PageOpened;
        public static event Action<UIPageHandle> PageClosed;
        public static event Action<UIPageHandle> TopPageChanged;
        private static UIPageHandle s_lastTop;
        private static readonly Dictionary<UIPageHandle, Yato.Resources.AwaitableCompletionSource> Closing =
            new Dictionary<UIPageHandle, Yato.Resources.AwaitableCompletionSource>();

        public static UIPageHandle TopPage
        {
            get
            {
                for (int layer = (int)UILayer.System; layer >= (int)UILayer.Normal; layer--)
                { var top = StackInstance.Peek((UILayer)layer); if (top != null) return top; }
                return null;
            }
        }
        private static void RaisePage(Action<UIPageHandle> handlers, UIPageHandle handle)
        {
            if (handlers == null) return;
            foreach (Action<UIPageHandle> callback in handlers.GetInvocationList())
                try { callback(handle); } catch (Exception error) { Log.Error("UIRouter", "Page observer failed", error); }
        }
        private static void PublishTop()
        {
            var top = TopPage;
            if (ReferenceEquals(top, s_lastTop)) return;
            s_lastTop = top; RaisePage(TopPageChanged, top);
        }
        private static void ResetUiExtensions()
        {
            PageOpened = null; PageClosed = null; TopPageChanged = null; s_lastTop = null;
            foreach (var pending in Closing.Values) pending.SetException(new OperationCanceledException("Router reset"));
            Closing.Clear();
        }
        /// <summary>Wait for optional exit transition, then use the existing synchronous Close contract.</summary>
        public static IAwaitable CloseAsync(UIPageHandle handle, CancellationToken ct = default)
        {
            if (handle != null && Closing.TryGetValue(handle, out var existing)) return existing;
            var source = new Yato.Resources.AwaitableCompletionSource();
            if (ct.IsCancellationRequested) { source.SetException(new OperationCanceledException(ct)); return source; }
            if (handle == null || !handle.IsValid) { source.SetResult(); return source; }
            var instance = handle.Context.Instance;
            var transition = instance != null ? instance.GetComponent<UIPageTransition>() : null;
            if (transition == null) { Close(handle); source.SetResult(); return source; }
            Closing.Add(handle, source);
            RuntimeHost.RunCoroutine(CloseRoutine(handle, transition, ct, source));
            return source;
        }
        private static IEnumerator CloseRoutine(UIPageHandle handle, UIPageTransition transition, CancellationToken ct,
            Yato.Resources.AwaitableCompletionSource source)
        {
            try
            {
                var fade = transition.Play(false, ct);
                while (!fade.IsDone) yield return null;
                Exception failure = null;
                try { fade.GetResult(); ct.ThrowIfCancellationRequested(); }
                catch (Exception error) { failure = error; }
                if (failure != null) { source.SetException(failure); yield break; }
                if (!handle.IsValid) { source.SetException(new OperationCanceledException("Page was closed")); yield break; }
                Close(handle); source.SetResult();
            }
            finally
            {
                Closing.Remove(handle);
                if (!source.IsDone) source.SetException(new OperationCanceledException("Close coroutine stopped"));
            }
        }
    }
}
