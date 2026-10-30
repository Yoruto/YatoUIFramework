using System;
using System.Collections.Generic;
using PureMVC.Interfaces;
using PureMVC.Patterns.Observer;

namespace PureMVC.Core
{
    /// <summary>Mediator 注册表与通知派发。</summary>
    public class View : IView
    {
        private static IView s_instance;

        protected readonly Dictionary<string, IMediator> m_mediatorMap =
            new Dictionary<string, IMediator>(StringComparer.Ordinal);

        protected readonly Dictionary<string, List<IObserver>> m_observerMap =
            new Dictionary<string, List<IObserver>>(StringComparer.Ordinal);

        internal View()
        {
        }

        public static IView GetInstance(Func<IView> factory = null)
        {
            if (s_instance == null)
            {
                s_instance = factory != null ? factory() : new View();
                if (s_instance == null)
                {
                    s_instance = new View();
                }
            }

            return s_instance;
        }

        public static void ResetInstance()
        {
            s_instance = null;
        }

        public virtual bool HasMediator(string mediatorName)
        {
            return !string.IsNullOrEmpty(mediatorName) && m_mediatorMap.ContainsKey(mediatorName);
        }

        public virtual void RegisterMediator(IMediator mediator)
        {
            if (mediator == null || string.IsNullOrEmpty(mediator.MediatorName))
            {
                return;
            }

            string mediatorName = mediator.MediatorName;
            if (m_mediatorMap.ContainsKey(mediatorName))
            {
                return;
            }

            m_mediatorMap[mediatorName] = mediator;

            string[] interests = mediator.ListNotificationInterests();
            if (interests != null && interests.Length > 0)
            {
                var observer = new Observer(new Action<INotification>(mediator.HandleNotification), mediator);
                for (int i = 0; i < interests.Length; i++)
                {
                    RegisterObserver(interests[i], observer);
                }
            }

            mediator.OnRegister();
        }

        public virtual IMediator RetrieveMediator(string mediatorName)
        {
            if (string.IsNullOrEmpty(mediatorName))
            {
                return null;
            }

            IMediator mediator;
            return m_mediatorMap.TryGetValue(mediatorName, out mediator) ? mediator : null;
        }

        public virtual IMediator RemoveMediator(string mediatorName)
        {
            if (string.IsNullOrEmpty(mediatorName))
            {
                return null;
            }

            IMediator mediator;
            if (!m_mediatorMap.TryGetValue(mediatorName, out mediator))
            {
                return null;
            }

            string[] interests = mediator.ListNotificationInterests();
            if (interests != null)
            {
                for (int i = 0; i < interests.Length; i++)
                {
                    RemoveObserver(interests[i], mediator);
                }
            }

            m_mediatorMap.Remove(mediatorName);
            mediator.OnRemove();
            return mediator;
        }

        public virtual void RegisterObserver(string notificationName, IObserver observer)
        {
            if (string.IsNullOrEmpty(notificationName) || observer == null)
            {
                return;
            }

            List<IObserver> observers;
            if (!m_observerMap.TryGetValue(notificationName, out observers))
            {
                observers = new List<IObserver>(4);
                m_observerMap[notificationName] = observers;
            }

            observers.Add(observer);
        }

        public virtual void RemoveObserver(string notificationName, object notifyContext)
        {
            if (string.IsNullOrEmpty(notificationName))
            {
                return;
            }

            List<IObserver> observers;
            if (!m_observerMap.TryGetValue(notificationName, out observers))
            {
                return;
            }

            for (int i = observers.Count - 1; i >= 0; i--)
            {
                if (observers[i].CompareNotifyContext(notifyContext))
                {
                    observers.RemoveAt(i);
                }
            }

            if (observers.Count == 0)
            {
                m_observerMap.Remove(notificationName);
            }
        }

        public virtual void NotifyObservers(INotification notification)
        {
            if (notification == null || string.IsNullOrEmpty(notification.Name))
            {
                return;
            }

            List<IObserver> observers;
            if (!m_observerMap.TryGetValue(notification.Name, out observers))
            {
                return;
            }

            // 快照派发：允许监听者在回调中退订/新增而不破坏本次遍历
            IObserver[] snapshot = observers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].NotifyObserver(notification);
            }
        }
    }
}
