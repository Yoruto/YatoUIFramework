namespace PureMVC.Interfaces
{
    /// <summary>中介者：连接 View 与业务逻辑，订阅通知。</summary>
    public interface IMediator : INotifier
    {
        string MediatorName { get; }

        object ViewComponent { get; set; }

        string[] ListNotificationInterests();

        void HandleNotification(INotification notification);

        void OnRegister();

        void OnRemove();
    }

    /// <summary>代理：持有数据模型，屏蔽数据来源。</summary>
    public interface IProxy : INotifier
    {
        string ProxyName { get; }

        object Data { get; set; }

        void OnRegister();

        void OnRemove();
    }

    /// <summary>命令：响应一次通知，执行一次业务动作。</summary>
    public interface ICommand : INotifier
    {
        void Execute(INotification notification);
    }
}
