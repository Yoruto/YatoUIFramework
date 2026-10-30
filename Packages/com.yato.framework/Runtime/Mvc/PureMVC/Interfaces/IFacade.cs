using System;

namespace PureMVC.Interfaces
{
    /// <summary>模型层：Proxy 注册表。</summary>
    public interface IModel
    {
        bool HasProxy(string proxyName);

        void RegisterProxy(IProxy proxy);

        IProxy RetrieveProxy(string proxyName);

        IProxy RemoveProxy(string proxyName);
    }

    /// <summary>视图层：Mediator 注册表与观察者派发。</summary>
    public interface IView
    {
        bool HasMediator(string mediatorName);

        void RegisterMediator(IMediator mediator);

        IMediator RetrieveMediator(string mediatorName);

        IMediator RemoveMediator(string mediatorName);

        void RegisterObserver(string notificationName, IObserver observer);

        void RemoveObserver(string notificationName, object notifyContext);

        void NotifyObservers(INotification notification);
    }

    /// <summary>控制层：通知名 → 命令工厂。</summary>
    public interface IController
    {
        bool HasCommand(string notificationName);

        void RegisterCommand(string notificationName, Func<ICommand> commandFunc);

        void RemoveCommand(string notificationName);

        void ExecuteCommand(INotification notification);
    }

    /// <summary>门面：Model / View / Controller 的统一入口。</summary>
    public interface IFacade : INotifier
    {
        void RegisterProxy(IProxy proxy);

        IProxy RetrieveProxy(string proxyName);

        IProxy RemoveProxy(string proxyName);

        bool HasProxy(string proxyName);

        void RegisterCommand(string notificationName, Func<ICommand> commandFunc);

        void RemoveCommand(string notificationName);

        bool HasCommand(string notificationName);

        void RegisterMediator(IMediator mediator);

        IMediator RetrieveMediator(string mediatorName);

        IMediator RemoveMediator(string mediatorName);

        bool HasMediator(string mediatorName);

        void NotifyObservers(INotification notification);
    }
}
