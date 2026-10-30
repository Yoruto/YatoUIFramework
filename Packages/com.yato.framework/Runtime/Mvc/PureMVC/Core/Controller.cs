using System;
using System.Collections.Generic;
using PureMVC.Interfaces;
using PureMVC.Patterns.Observer;

namespace PureMVC.Core
{
    /// <summary>通知名到命令工厂的映射，并在 View 上注册执行观察者。</summary>
    public class Controller : IController
    {
        private static IController s_instance;

        protected readonly Dictionary<string, Func<ICommand>> m_commandMap =
            new Dictionary<string, Func<ICommand>>(StringComparer.Ordinal);

        protected readonly IView m_view;

        internal Controller(IView view)
        {
            m_view = view;
        }

        public static IController GetInstance(Func<IController> factory = null)
        {
            if (s_instance == null)
            {
                s_instance = factory != null ? factory() : null;
            }

            return s_instance;
        }

        public static void ResetInstance()
        {
            s_instance = null;
        }

        public virtual bool HasCommand(string notificationName)
        {
            return !string.IsNullOrEmpty(notificationName) && m_commandMap.ContainsKey(notificationName);
        }

        public virtual void RegisterCommand(string notificationName, Func<ICommand> commandFunc)
        {
            if (string.IsNullOrEmpty(notificationName) || commandFunc == null)
            {
                return;
            }

            if (!m_commandMap.ContainsKey(notificationName) && m_view != null)
            {
                m_view.RegisterObserver(
                    notificationName,
                    new Observer(new Action<INotification>(ExecuteCommand), this));
            }

            m_commandMap[notificationName] = commandFunc;
        }

        public virtual void RemoveCommand(string notificationName)
        {
            if (string.IsNullOrEmpty(notificationName) || !m_commandMap.ContainsKey(notificationName))
            {
                return;
            }

            m_commandMap.Remove(notificationName);
            if (m_view != null)
            {
                m_view.RemoveObserver(notificationName, this);
            }
        }

        public virtual void ExecuteCommand(INotification notification)
        {
            if (notification == null || string.IsNullOrEmpty(notification.Name))
            {
                return;
            }

            Func<ICommand> factory;
            if (!m_commandMap.TryGetValue(notification.Name, out factory) || factory == null)
            {
                return;
            }

            ICommand command = factory();
            if (command != null)
            {
                command.Execute(notification);
            }
        }
    }
}
