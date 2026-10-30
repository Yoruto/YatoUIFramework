using PureMVC.Patterns.Mediator;

namespace Yato.UI
{
    /// <summary>
    /// 页面中介者（旧 BaseMediator 的替代）：不再创建 GameObject，只做生命周期转发与业务逻辑。
    /// 子类必须提供 <c>(string mediatorName)</c> 构造函数，用于多实例命名（DEF-05）。
    /// </summary>
    public class UIPageMediator : Mediator, IUIPage
    {
        public UIPageMediator()
        {
        }

        public UIPageMediator(string mediatorName) : base(mediatorName)
        {
        }

        /// <summary>所属页面上下文，由 UIPageFactory 注入。</summary>
        public UIPageContext Context { get; internal set; }

        /// <summary>当前视图，由 UIPageFactory 注入。</summary>
        public UIPageView View { get; internal set; }

        /// <summary>子类可返回自己的数据代理；默认无 Proxy。</summary>
        protected virtual UIPageProxy CreateProxy()
        {
            return null;
        }

        public virtual void OnCreate()
        {
            if (View != null)
            {
                View.OnCreate();
            }
        }

        public virtual void OnOpen(object args)
        {
            if (View != null)
            {
                View.OnOpen(args);
            }
        }

        public virtual void OnCover()
        {
            if (View != null)
            {
                View.OnCover();
            }
        }

        public virtual void OnReveal()
        {
            if (View != null)
            {
                View.OnReveal();
            }
        }

        public virtual void OnClose(bool destroy)
        {
            if (View != null)
            {
                View.OnClose(destroy);
            }
        }

        /// <summary>
        /// 由工厂调用：创建并返回本页面的 Proxy（无则返回 null）。
        /// UIRouter 在注册阶段统一注册，避免时序错乱。
        /// </summary>
        internal UIPageProxy CreateProxyInternal()
        {
            UIPageProxy proxy = CreateProxy();
            if (proxy != null)
            {
                proxy.Context = Context;
                Context.Proxy = proxy;
            }

            return proxy;
        }
    }

    /// <summary>强类型中介者：省去业务侧强制类型转换（T-P1-02）。</summary>
    public class UIPageMediator<TView> : UIPageMediator where TView : UIPageView
    {
        public UIPageMediator()
        {
        }

        public UIPageMediator(string mediatorName) : base(mediatorName)
        {
        }

        /// <summary>强类型视图。</summary>
        public new TView View
        {
            get { return base.View as TView; }
        }
    }
}
