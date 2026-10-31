namespace Yato.UI
{
    /// <summary>UI 层级。每个层级独立 Canvas，避免整层重建（文档 7.1）。</summary>
    public enum UILayer
    {
        /// <summary>背景层，sortingOrder 0。</summary>
        Background = 0,

        /// <summary>常规页面层，sortingOrder 100。</summary>
        Normal = 1,

        /// <summary>弹窗层，sortingOrder 200。</summary>
        Popup = 2,

        /// <summary>飘字 / Toast 层，sortingOrder 300。</summary>
        Tips = 3,

        /// <summary>系统层（Loading / 断线重连），sortingOrder 400，不响应返回键。</summary>
        System = 4
    }
}
