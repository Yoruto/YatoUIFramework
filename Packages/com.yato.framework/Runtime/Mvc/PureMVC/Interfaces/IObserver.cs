using System;

namespace PureMVC.Interfaces
{
    /// <summary>观察者：持有回调与上下文，由 View 统一派发。</summary>
    public interface IObserver
    {
        void SetNotifyMethod(Action<INotification> notifyMethod);

        void SetNotifyContext(object notifyContext);

        void NotifyObserver(INotification notification);

        bool CompareNotifyContext(object obj);
    }
}
