namespace Yato.UI
{
    /// <summary>页面句柄：关闭页面的唯一凭据，取代旧的字符串命令。</summary>
    public sealed class UIPageHandle
    {
        internal UIPageHandle(UIPageContext context)
        {
            Context = context;
        }

        public UIPageContext Context { get; private set; }

        public string PageId
        {
            get { return Context != null && Context.Def != null ? Context.Def.PageId : string.Empty; }
        }

        /// <summary>是否仍然可用（已打开或已缓存）。</summary>
        public bool IsValid { get; internal set; }

        /// <summary>等价于 <c>UIRouter.Close(this)</c>。</summary>
        public void Close()
        {
            UIRouter.Close(this);
        }
    }
}
