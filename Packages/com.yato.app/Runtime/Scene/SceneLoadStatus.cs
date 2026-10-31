namespace Yato.App
{
    /// <summary>场景操作的结果状态（D03 冻结的五态）。</summary>
    public enum SceneLoadStatus
    {
        /// <summary>加载完成且（若配置了初始化器）已 Ready。</summary>
        Success = 0,

        /// <summary>已有场景操作在进行，立即返回，不排队。</summary>
        Busy = 1,

        /// <summary>真正开始 Unity 加载之前被取消；开始之后不承诺强制取消。</summary>
        Canceled = 2,

        /// <summary>场景名非法或不在可加载列表（默认对应 Build Settings）。</summary>
        InvalidScene = 3,

        /// <summary>加载/激活/Ready/卸载阶段失败；框架不自动回滚。</summary>
        Failed = 4
    }

    /// <summary>失败/取消发生在哪个阶段，便于定位。</summary>
    public enum SceneLoadStage
    {
        None = 0,
        Validate = 1,
        CancelCheck = 2,
        SceneLoad = 3,
        Loaded = 4,
        Ready = 5,
        Unload = 6
    }
}
