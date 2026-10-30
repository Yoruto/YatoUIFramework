using PureMVC.Interfaces;
using Yato.UI;

namespace Yato.FrameworkDemo
{
    /// <summary>
    /// 示例中介者：泛型绑定视图，Proxy 通过 CreateProxy 声明。
    /// 注意必须提供 (string mediatorName) 构造，多实例时才能正确命名。
    /// </summary>
    public class ExampleMediator : UIPageMediator<ExamplePageView>
    {
        public ExampleMediator()
        {
        }

        public ExampleMediator(string mediatorName) : base(mediatorName)
        {
        }

        public ExampleProxy ExampleProxy
        {
            get { return Context != null ? Context.Proxy as ExampleProxy : null; }
        }

        protected override UIPageProxy CreateProxy()
        {
            // ProxyName 由框架保证唯一：{PageId}Proxy 或 {PageId}#{InstanceId}Proxy
            return new ExampleProxy(string.Format("{0}Proxy", Context.InstanceName));
        }

        public override string[] ListNotificationInterests()
        {
            return new[] { FrameworkNotifications.UIPageClosed };
        }

        public override void HandleNotification(INotification notification)
        {
            if (notification.Name == FrameworkNotifications.UIPageClosed)
            {
                // 页面关闭后的收尾逻辑
            }
        }

        public override void OnOpen(object args)
        {
            base.OnOpen(args); // 转发给 View

            ExampleProxy proxy = ExampleProxy;
            if (proxy != null)
            {
                proxy.BindModel(string.Format("counter={0}", proxy.Counter));
            }
        }
    }
}
