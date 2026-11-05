using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>Thread-safe completion; observers run on the Unity main thread, outside the state lock.</summary>
    public sealed class AwaitableCompletionSource : IAwaitable
    {
        private readonly AwaitableCompletionSource<bool> m_inner = new AwaitableCompletionSource<bool>();
        public bool IsDone => m_inner.IsDone;
        public bool IsFaulted => m_inner.IsFaulted;
        public event Action Completed { add { m_inner.Completed += value; } remove { m_inner.Completed -= value; } }
        public void SetResult() => m_inner.SetResult(true);
        public void SetException(Exception error) => m_inner.SetException(error);
        public void GetResult() { m_inner.GetResult(); }
    }

    /// <summary>
    /// Create on the Unity main thread. Before a RuntimeHost exists (for example, EditMode tests),
    /// the creating thread is used for inline callbacks; other threads still queue through the dispatcher.
    /// </summary>
    public sealed class AwaitableCompletionSource<T> : IAwaitable<T>
    {
        private readonly object m_gate = new object();
        private readonly int m_creationThread = Thread.CurrentThread.ManagedThreadId;
        private T m_result;
        private Exception m_error;
        private bool m_done;
        private Action m_completed;

        public bool IsDone { get { lock (m_gate) return m_done; } }
        public bool IsFaulted { get { lock (m_gate) return m_error != null; } }

        public event Action Completed
        {
            add
            {
                lock (m_gate)
                {
                    if (!m_done) { m_completed += value; return; }
                }
                Dispatch(value);
            }
            remove { lock (m_gate) m_completed -= value; }
        }

        public void SetResult(T result) => Complete(result, null);
        public void SetException(Exception error) => Complete(default(T), error ?? new InvalidOperationException("awaitable 以未知错误结束"));

        public T GetResult()
        {
            T result;
            Exception error;
            lock (m_gate)
            {
                if (!m_done) throw new InvalidOperationException("awaitable 尚未完成，不能读取结果");
                result = m_result;
                error = m_error;
            }
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
            return result;
        }

        void IAwaitable.GetResult() { GetResult(); }

        private void Complete(T result, Exception error)
        {
            Action handler;
            lock (m_gate)
            {
                if (m_done) return;
                m_result = result;
                m_error = error;
                m_done = true;
                handler = m_completed;
                m_completed = null;
            }
            Dispatch(handler);
        }

        private void Dispatch(Action handler)
        {
            if (handler == null) return;
            if (MainThreadDispatcher.IsMainThread ||
                (!MainThreadDispatcher.IsInitialized && Thread.CurrentThread.ManagedThreadId == m_creationThread))
            {
                InvokeSafely(handler);
                return;
            }
            // Completion belongs to the original operation, including after host shutdown.
            // Resource/UI owners apply their own lifetime checks before accepting late results.
            MainThreadDispatcher.PostCompletion(() => InvokeSafely(handler));
        }

        private static void InvokeSafely(Action handler)
        {
            foreach (Action callback in handler.GetInvocationList())
                try { callback(); }
                catch (Exception e) { Log.Error("Awaitable", "完成回调异常", e); }
        }
    }
}
