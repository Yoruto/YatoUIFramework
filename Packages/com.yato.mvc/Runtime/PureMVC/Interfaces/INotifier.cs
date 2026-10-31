namespace PureMVC.Interfaces
{
    /// <summary>可发送通知的角色（Mediator / Proxy / Command / Facade）。</summary>
    public interface INotifier
    {
        void SendNotification(string notificationName);

        void SendNotification(string notificationName, object body);

        void SendNotification(string notificationName, object body, string type);

        void InitializeNotifier(string key);
    }
}
