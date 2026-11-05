namespace Yato.Settings
{
    /// <summary>设置加载结果状态（D05）：缺文件属于正常情况，损坏/非法必须明确报告且不覆盖磁盘。</summary>
    public enum SettingsLoadStatus
    {
        NotLoaded = 0,

        /// <summary>已从用户文件加载并通过校验。</summary>
        Loaded = 1,

        /// <summary>用户文件不存在，使用默认值（首次启动的正常路径）。</summary>
        DefaultsUsed = 2,

        /// <summary>文件内容无法反序列化；保留原文件，运行时暂用默认值。</summary>
        Corrupted = 3,

        /// <summary>用户数据未通过游戏自定义校验；保留原文件，运行时暂用默认值。</summary>
        Invalid = 4,

        /// <summary>读取失败（IO/权限等）；保留原文件，运行时暂用默认值。文件忙单独抛出异常。</summary>
        Failed = 5,

        /// <summary>已有更新的加载或运行期修改，本次结果未发布，当前状态保持不变。</summary>
        Superseded = 6
    }
}
