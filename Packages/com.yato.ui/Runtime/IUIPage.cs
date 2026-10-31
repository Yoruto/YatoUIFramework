namespace Yato.UI
{
    /// <summary>页面生命周期（文档 3.6/7.2）。</summary>
    public interface IUIPage
    {
        /// <summary>实例化完成后调用，只执行一次。</summary>
        void OnCreate();

        /// <summary>每次显示时调用。</summary>
        void OnOpen(object args);

        /// <summary>被上层页面遮挡。</summary>
        void OnCover();

        /// <summary>重新露出。</summary>
        void OnReveal();

        /// <summary>关闭；<paramref name="destroy"/> 为 true 表示随后销毁实例。</summary>
        void OnClose(bool destroy);
    }
}
