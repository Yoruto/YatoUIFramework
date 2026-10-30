using PureMVC.Patterns.Proxy;

namespace Yato.UI
{
    /// <summary>
    /// 页面数据代理（旧 BaseProxy 的替代）。
    /// ProxyName 由工厂显式传入 <c>{PageId}Proxy</c>，不再出现共用空 key（DEF-03）。
    /// </summary>
    public class UIPageProxy : Proxy
    {
        public UIPageProxy(string proxyName) : base(proxyName)
        {
        }

        public UIPageProxy(string proxyName, object data) : base(proxyName, data)
        {
        }

        /// <summary>所属页面上下文。</summary>
        public UIPageContext Context { get; internal set; }

        /// <summary>绑定数据模型后刷新视图的约定。</summary>
        public virtual void BindModel(object model)
        {
            Data = model;
            RefreshView();
        }

        /// <summary>由子类实现：把 Data 刷新到视图。</summary>
        public virtual void RefreshView()
        {
        }
    }
}
