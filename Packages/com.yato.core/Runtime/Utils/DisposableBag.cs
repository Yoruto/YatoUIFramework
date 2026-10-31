using System;
using System.Collections.Generic;

namespace Yato.Core
{
    /// <summary>
    /// 一组 <see cref="IDisposable"/> 的聚合释放器，页面关闭时一次性退订/释放。
    /// Dispose 按加入的逆序执行，单个释放失败不会中断其余释放。
    /// </summary>
    public sealed class DisposableBag : IDisposable
    {
        private readonly List<IDisposable> m_items = new List<IDisposable>(8);
        private bool m_disposed;

        public bool IsDisposed
        {
            get { return m_disposed; }
        }

        public int Count
        {
            get { return m_items.Count; }
        }

        public void Add(IDisposable item)
        {
            if (item == null)
            {
                return;
            }

            if (m_disposed)
            {
                // 已释放后再加入：立即释放，避免泄漏
                SafeDispose(item);
                return;
            }

            m_items.Add(item);
        }

        /// <summary>把「退订动作」包装成 IDisposable 加入。</summary>
        public void Add(Action unsubscribe)
        {
            if (unsubscribe == null)
            {
                return;
            }

            Add(new ActionDisposable(unsubscribe));
        }

        /// <summary>立即释放全部条目，但不标记自身已释放。</summary>
        public void Clear()
        {
            for (int i = m_items.Count - 1; i >= 0; i--)
            {
                SafeDispose(m_items[i]);
            }

            m_items.Clear();
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            Clear();
        }

        private static void SafeDispose(IDisposable item)
        {
            try
            {
                item.Dispose();
            }
            catch (Exception e)
            {
                Log.Error("DisposableBag", "释放条目异常", e);
            }
        }

        private sealed class ActionDisposable : IDisposable
        {
            private Action m_action;

            public ActionDisposable(Action action)
            {
                m_action = action;
            }

            public void Dispose()
            {
                Action action = m_action;
                m_action = null;
                if (action != null)
                {
                    action();
                }
            }
        }
    }
}
