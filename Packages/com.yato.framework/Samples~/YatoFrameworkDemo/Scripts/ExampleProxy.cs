using Yato.UI;

namespace Yato.FrameworkDemo
{
    /// <summary>示例数据代理：BindModel 后自动 RefreshView。</summary>
    public class ExampleProxy : UIPageProxy
    {
        public int Counter { get; private set; }

        public ExampleProxy(string proxyName) : base(proxyName)
        {
        }

        public void Increment()
        {
            Counter++;
            RefreshView();
        }

        public override void RefreshView()
        {
            // 把 Data 推给视图；这里保持示例最小化
            ExamplePageView view = Context != null ? Context.View as ExamplePageView : null;
            if (view != null)
            {
                view.Refresh(Data);
            }

            SendNotification(FrameworkNotifications.UIPageOpened, Data);
        }
    }
}
