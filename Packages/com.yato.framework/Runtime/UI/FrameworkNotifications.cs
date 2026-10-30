namespace Yato.UI
{
    /// <summary>
    /// 框架级通知常量。业务通知请放在各自的业务命名空间，
    /// 不要再混进框架（DEF-11）。
    /// </summary>
    public static class FrameworkNotifications
    {
        /// <summary>显示飘字，Body 为文本。</summary>
        public const string UIToast = "Yato.UI.Toast";

        /// <summary>请求返回上一页（Android 返回键 / ESC）。</summary>
        public const string UIBack = "Yato.UI.Back";

        /// <summary>页面已打开，Body 为 <see cref="UIPageHandle"/>。</summary>
        public const string UIPageOpened = "Yato.UI.PageOpened";

        /// <summary>页面已关闭，Body 为页面 id。</summary>
        public const string UIPageClosed = "Yato.UI.PageClosed";

        /// <summary>页面以错误结束，Body 为异常。</summary>
        public const string UIPageFailed = "Yato.UI.PageFailed";
    }
}
