using PureMVC.Interfaces;
using UnityEngine;

namespace PureMVC.Patterns.Observer
{
    /// <summary>
    /// 通知发送者的默认实现。Facade 未初始化时记录错误而不是静默丢弃，
    /// 便于在启动顺序出错时第一时间定位。
    /// </summary>
    public class Notifier : INotifier
    {
        protected string m_multitonKey;

        /// <summary>当前门面；未初始化时返回 null。</summary>
        protected IFacade Facade
        {
            get { return PureMVC.Patterns.Facade.Facade.PeekInstance(); }
        }

        public virtual void SendNotification(string notificationName)
        {
            SendNotification(notificationName, null, null);
        }

        public virtual void SendNotification(string notificationName, object body)
        {
            SendNotification(notificationName, body, null);
        }

        public virtual void SendNotification(string notificationName, object body, string type)
        {
            IFacade facade = Facade;
            if (facade == null)
            {
                Debug.LogErrorFormat(
                    "[PureMVC] Facade 尚未初始化，通知被丢弃：{0}（请先访问 Facade.GetInstance）",
                    notificationName);
                return;
            }

            facade.NotifyObservers(new Notification(notificationName, body, type));
        }

        /// <summary>单核模式下仅记录 key，保留多核扩展位。</summary>
        public virtual void InitializeNotifier(string key)
        {
            m_multitonKey = key;
        }
    }
}
