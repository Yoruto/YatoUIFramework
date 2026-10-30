namespace PureMVC.Interfaces
{
    /// <summary>通知对象：观察者之间传递消息的载体。</summary>
    public interface INotification
    {
        /// <summary>通知名（命令与 Mediator 按此订阅）。</summary>
        string Name { get; }

        /// <summary>消息体。</summary>
        object Body { get; set; }

        /// <summary>消息类型，用于同名的多态区分。</summary>
        string Type { get; set; }
    }
}
