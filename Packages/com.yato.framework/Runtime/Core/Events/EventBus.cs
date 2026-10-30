using System;
using System.Collections.Generic;
using System.Threading;

namespace Yato.Core
{
    /// <summary>
    /// 类型化事件总线默认实现。
    /// 派发时对监听列表做快照，监听者可以在回调里安全地订阅/退订；
    /// 单个监听者抛异常不会中断其余监听者（DEF-14/DEF-15）。
    /// </summary>
    public sealed class EventBus : IEventBus
    {
        private readonly Dictionary<Type, List<Delegate>> m_handlers =
            new Dictionary<Type, List<Delegate>>();

        private readonly object m_gate = new object();

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (handler == null)
            {
                return EmptyDisposable.Instance;
            }

            Type eventType = typeof(T);
            lock (m_gate)
            {
                List<Delegate> list;
                if (!m_handlers.TryGetValue(eventType, out list))
                {
                    list = new List<Delegate>(4);
                    m_handlers[eventType] = list;
                }

                list.Add(handler);
            }

            return new Subscription(this, eventType, handler);
        }

        public void Publish<T>(T evt)
        {
            Delegate[] snapshot;
            lock (m_gate)
            {
                List<Delegate> list;
                if (!m_handlers.TryGetValue(typeof(T), out list) || list.Count == 0)
                {
                    return;
                }

                snapshot = list.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                var handler = snapshot[i] as Action<T>;
                if (handler == null)
                {
                    continue;
                }

                try
                {
                    handler(evt);
                }
                catch (Exception e)
                {
                    Log.Error(
                        "EventBus",
                        string.Format("事件处理异常：{0} ← {1}", typeof(T).FullName, Describe(handler)),
                        e);
                }
            }
        }

        public void PublishOnMainThread<T>(T evt)
        {
            if (MainThreadDispatcher.IsMainThread)
            {
                Publish(evt);
                return;
            }

            MainThreadDispatcher.Post(delegate { Publish(evt); });
        }

        public void Clear()
        {
            lock (m_gate)
            {
                m_handlers.Clear();
            }
        }

        /// <summary>当前某事件的订阅数量（诊断用）。</summary>
        public int SubscriberCount<T>()
        {
            lock (m_gate)
            {
                List<Delegate> list;
                return m_handlers.TryGetValue(typeof(T), out list) ? list.Count : 0;
            }
        }

        private void Unsubscribe(Type eventType, Delegate handler)
        {
            lock (m_gate)
            {
                List<Delegate> list;
                if (!m_handlers.TryGetValue(eventType, out list))
                {
                    return;
                }

                list.Remove(handler);
                if (list.Count == 0)
                {
                    m_handlers.Remove(eventType);
                }
            }
        }

        private static string Describe(Delegate handler)
        {
            if (handler == null || handler.Method == null)
            {
                return "?";
            }

            Type declaringType = handler.Method.DeclaringType;
            return string.Format("{0}.{1}", declaringType != null ? declaringType.Name : "?", handler.Method.Name);
        }

        private sealed class Subscription : IDisposable
        {
            private EventBus m_bus;
            private readonly Type m_eventType;
            private readonly Delegate m_handler;
            private int m_disposed;

            public Subscription(EventBus bus, Type eventType, Delegate handler)
            {
                m_bus = bus;
                m_eventType = eventType;
                m_handler = handler;
            }

            public void Dispose()
            {
                // 只退订一次（DEF-14：重复 Dispose 不应产生副作用）
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }

                EventBus bus = m_bus;
                m_bus = null;
                if (bus != null)
                {
                    bus.Unsubscribe(m_eventType, m_handler);
                }
            }
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();

            public void Dispose()
            {
            }
        }
    }
}
