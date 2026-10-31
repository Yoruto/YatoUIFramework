using System;
using System.Threading;
using System.Threading.Tasks;

namespace Yato.Settings
{
    /// <summary>
    /// 强类型设置服务（D05）：默认值 → 用户文件 → 自定义校验 → 发布。
    /// <para>只负责状态与持久化；不直接控制音频、画面、输入或 UI，由游戏层订阅 <see cref="Changed"/> 应用效果。</para>
    /// <para>设置变化不会自动落盘，必须在安全时机显式 <see cref="Save()"/>。</para>
    /// </summary>
    public interface ISettingsService<T> where T : class
    {
        /// <summary>
        /// 当前设置快照（运行期副本，绝不指向原始默认资源）。
        /// 每次读取都返回副本：修改返回值不会影响服务状态，也不会触发 <see cref="Changed"/>。
        /// </summary>
        T Current { get; }

        /// <summary>是否至少完成过一次加载（或加载失败后回退默认值）。</summary>
        bool IsLoaded { get; }

        /// <summary>最近一次加载结果。</summary>
        SettingsLoadResult LastLoadResult { get; }

        /// <summary>游戏提供的数据相对路径（如 <c>settings/game.json</c>），由框架校验。</summary>
        string RelativePath { get; }

        /// <summary>设置被发布（加载、受控修改、恢复默认）后触发。</summary>
        event Action<T> Changed;

        /// <summary>同步加载：缺文件用默认值，损坏/非法保留原文件并报告。</summary>
        SettingsLoadResult Load();

        Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>同步保存当前设置；校验不通过时抛 <see cref="SettingsValidationException"/> 且不写盘。</summary>
        void Save();

        Task SaveAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>受控修改：修改后统一发布变化事件（直接改字段不会触发通知）。</summary>
        void Apply(Action<T> mutate);

        /// <summary>恢复默认值并发布；返回新的当前设置。</summary>
        T ResetToDefaults();

        /// <summary>取得默认值副本（用于设置界面预览，不影响当前值）。</summary>
        T CreateDefaultsSnapshot();
    }
}
