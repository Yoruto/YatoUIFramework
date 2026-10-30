using UnityEngine;

namespace Yato.UI
{
    /// <summary>
    /// 页面视图基类（旧 BaseUIView 的替代）。
    /// 作为 MonoBehaviour 挂在预制体上，绑定在 Awake 完成，不再在构造期回调（DEF-07）。
    /// </summary>
    public class UIPageView : MonoBehaviour, IUIPage
    {
        /// <summary>所属页面上下文，由 UIPageFactory 注入。</summary>
        public UIPageContext Context { get; internal set; }

        public UIPageDef Def
        {
            get { return Context != null ? Context.Def : null; }
        }

        /// <summary>绑定组件、缓存引用（只执行一次）。</summary>
        protected virtual void Awake()
        {
        }

        public virtual void OnCreate()
        {
        }

        public virtual void OnOpen(object args)
        {
        }

        public virtual void OnCover()
        {
        }

        public virtual void OnReveal()
        {
        }

        public virtual void OnClose(bool destroy)
        {
        }

        /// <summary>关闭自身（等价于 UIRouter.Close(handle)）。</summary>
        public void Close()
        {
            if (Context != null && Context.Handle != null)
            {
                Context.Handle.Close();
            }
        }
    }
}
