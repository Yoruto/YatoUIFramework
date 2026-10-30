using System;
using System.Runtime.ExceptionServices;

namespace Yato.Resources
{
    /// <summary>
    /// <see cref="IAwaitable"/> 的默认实现：由资源层在加载完成/失败时写入结果。
    /// 回调与读取一律在主线程进行。
    /// 注意：Unity 6 起引擎自带 <c>UnityEngine.AwaitableCompletionSource&lt;T&gt;</c>，
    /// 在 <c>Yato.Resources</c> 命名空间之外的代码里引用时请写全限定名。
    /// </summary>
    public sealed class AwaitableCompletionSource : IAwaitable
    {
        private Exception m_error;
        private bool m_done;
        private Action m_completed;

        public bool IsDone
        {
            get { return m_done; }
        }

        public bool IsFaulted
        {
            get { return m_error != null; }
        }

        public event Action Completed
        {
            add
            {
                if (m_done)
                {
                    InvokeSafely(value);
                    return;
                }

                m_completed += value;
            }
            remove { m_completed -= value; }
        }

        public void SetResult()
        {
            Complete(null);
        }

        public void SetException(Exception error)
        {
            Complete(error ?? new InvalidOperationException("awaitable 以未知错误结束"));
        }

        public void GetResult()
        {
            if (!m_done)
            {
                throw new InvalidOperationException("awaitable 尚未完成，不能读取结果");
            }

            if (m_error != null)
            {
                ExceptionDispatchInfo.Capture(m_error).Throw();
            }
        }

        private void Complete(Exception error)
        {
            if (m_done)
            {
                return;
            }

            m_error = error;
            m_done = true;

            Action handler = m_completed;
            m_completed = null;
            InvokeSafely(handler);
        }

        private static void InvokeSafely(Action handler)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception e)
            {
                Yato.Core.Log.Error("Awaitable", "完成回调异常", e);
            }
        }
    }

    /// <summary>带返回值的 <see cref="IAwaitable{T}"/> 默认实现。</summary>
    public sealed class AwaitableCompletionSource<T> : IAwaitable<T>
    {
        private T m_result;
        private Exception m_error;
        private bool m_done;
        private Action m_completed;

        public bool IsDone
        {
            get { return m_done; }
        }

        public bool IsFaulted
        {
            get { return m_error != null; }
        }

        public event Action Completed
        {
            add
            {
                if (m_done)
                {
                    InvokeSafely(value);
                    return;
                }

                m_completed += value;
            }
            remove { m_completed -= value; }
        }

        public void SetResult(T result)
        {
            m_result = result;
            Complete(null);
        }

        public void SetException(Exception error)
        {
            Complete(error ?? new InvalidOperationException("awaitable 以未知错误结束"));
        }

        public T GetResult()
        {
            if (!m_done)
            {
                throw new InvalidOperationException("awaitable 尚未完成，不能读取结果");
            }

            if (m_error != null)
            {
                ExceptionDispatchInfo.Capture(m_error).Throw();
            }

            return m_result;
        }

        void IAwaitable.GetResult()
        {
            GetResult();
        }

        private void Complete(Exception error)
        {
            if (m_done)
            {
                return;
            }

            m_error = error;
            m_done = true;

            Action handler = m_completed;
            m_completed = null;
            InvokeSafely(handler);
        }

        private static void InvokeSafely(Action handler)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception e)
            {
                Yato.Core.Log.Error("Awaitable", "完成回调异常", e);
            }
        }
    }
}
