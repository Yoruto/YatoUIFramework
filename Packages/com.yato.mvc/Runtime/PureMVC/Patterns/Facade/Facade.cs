using System;
using PureMVC.Core;
using PureMVC.Interfaces;
using PureMVC.Patterns.Observer;
using UnityEngine;

namespace PureMVC.Patterns.Facade
{
    /// <summary>
    /// 单核门面。构造时若已存在实例会抛异常，因此请统一通过
    /// <see cref="GetInstance"/> 获取，不要直接 new。
    /// </summary>
    public class Facade : Notifier, IFacade
    {
        protected const string SingletonMsg = "Facade Singleton already constructed!";

        private static IFacade s_instance;

        protected IController m_controller;
        protected IModel m_model;
        protected IView m_view;

        public Facade()
        {
            if (s_instance != null)
            {
                throw new InvalidOperationException(SingletonMsg);
            }

            s_instance = this;
            InitializeFacade();
        }

        /// <summary>获取（必要时创建）门面实例。</summary>
        public static IFacade GetInstance(Func<IFacade> facadeFunc)
        {
            if (s_instance == null && facadeFunc != null)
            {
                IFacade created = facadeFunc();
                if (created != null && s_instance == null)
                {
                    s_instance = created;
                }
            }

            return s_instance;
        }

        /// <summary>只读取，不创建。供 Notifier 发送通知时使用。</summary>
        public static IFacade PeekInstance()
        {
            return s_instance;
        }

        /// <summary>
        /// 全量重置门面与 Model/View/Controller 的静态状态。
        /// 关闭域重载重复进入 Play 时，旧实例不会残留（DEF-02）。
        /// </summary>
        public static void ResetAll()
        {
            s_instance = null;
            Model.ResetInstance();
            View.ResetInstance();
            Controller.ResetInstance();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayMode()
        {
            ResetAll();
        }

        protected virtual void InitializeFacade()
        {
            InitializeModel();
            InitializeView();
            InitializeController();
        }

        protected virtual void InitializeModel()
        {
            if (m_model != null)
            {
                return;
            }

            m_model = Model.GetInstance(() => new Model());
        }

        protected virtual void InitializeView()
        {
            if (m_view != null)
            {
                return;
            }

            m_view = View.GetInstance(() => new View());
        }

        protected virtual void InitializeController()
        {
            if (m_controller != null)
            {
                return;
            }

            m_controller = Controller.GetInstance(() => new Controller(m_view));
        }

        #region Proxy

        public virtual bool HasProxy(string proxyName)
        {
            return m_model != null && m_model.HasProxy(proxyName);
        }

        public virtual void RegisterProxy(IProxy proxy)
        {
            if (m_model != null)
            {
                m_model.RegisterProxy(proxy);
            }
        }

        public virtual IProxy RetrieveProxy(string proxyName)
        {
            return m_model != null ? m_model.RetrieveProxy(proxyName) : null;
        }

        public virtual IProxy RemoveProxy(string proxyName)
        {
            return m_model != null ? m_model.RemoveProxy(proxyName) : null;
        }

        #endregion

        #region Command

        public virtual bool HasCommand(string notificationName)
        {
            return m_controller != null && m_controller.HasCommand(notificationName);
        }

        public virtual void RegisterCommand(string notificationName, Func<ICommand> commandFunc)
        {
            if (m_controller != null)
            {
                m_controller.RegisterCommand(notificationName, commandFunc);
            }
        }

        public virtual void RemoveCommand(string notificationName)
        {
            if (m_controller != null)
            {
                m_controller.RemoveCommand(notificationName);
            }
        }

        #endregion

        #region Mediator

        public virtual bool HasMediator(string mediatorName)
        {
            return m_view != null && m_view.HasMediator(mediatorName);
        }

        public virtual void RegisterMediator(IMediator mediator)
        {
            if (m_view != null)
            {
                m_view.RegisterMediator(mediator);
            }
        }

        public virtual IMediator RetrieveMediator(string mediatorName)
        {
            return m_view != null ? m_view.RetrieveMediator(mediatorName) : null;
        }

        public virtual IMediator RemoveMediator(string mediatorName)
        {
            return m_view != null ? m_view.RemoveMediator(mediatorName) : null;
        }

        #endregion

        public virtual void NotifyObservers(INotification notification)
        {
            if (m_view != null)
            {
                m_view.NotifyObservers(notification);
            }
        }
    }
}
