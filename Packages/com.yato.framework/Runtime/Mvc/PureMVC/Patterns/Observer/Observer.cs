using System;
using PureMVC.Interfaces;

namespace PureMVC.Patterns.Observer
{
    /// <summary>观察者的默认实现：回调 + 上下文。</summary>
    public class Observer : IObserver
    {
        private Action<INotification> m_notifyMethod;
        private object m_notifyContext;

        public Observer(Action<INotification> notifyMethod, object notifyContext)
        {
            m_notifyMethod = notifyMethod;
            m_notifyContext = notifyContext;
        }

        public virtual void SetNotifyMethod(Action<INotification> notifyMethod)
        {
            m_notifyMethod = notifyMethod;
        }

        public virtual void SetNotifyContext(object notifyContext)
        {
            m_notifyContext = notifyContext;
        }

        public virtual void NotifyObserver(INotification notification)
        {
            if (m_notifyContext == null || m_notifyMethod == null)
            {
                return;
            }

            m_notifyMethod(notification);
        }

        public virtual bool CompareNotifyContext(object obj)
        {
            return ReferenceEquals(m_notifyContext, obj) || Equals(m_notifyContext, obj);
        }
    }
}
