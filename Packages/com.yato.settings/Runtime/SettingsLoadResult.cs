using System;

namespace Yato.Settings
{
    /// <summary>一次设置加载的结果：状态 + 可读说明 + 原始异常（若有）。</summary>
    public sealed class SettingsLoadResult
    {
        private SettingsLoadResult()
        {
        }

        public SettingsLoadStatus Status { get; private set; }

        /// <summary>损坏/非法/失败时的原始异常；正常路径为 null。</summary>
        public Exception Error { get; private set; }

        /// <summary>可读说明，便于日志与 UI 提示。</summary>
        public string Message { get; private set; }

        /// <summary>是否使用了默认值而非用户配置。</summary>
        public bool UsedDefaults
        {
            get { return Status != SettingsLoadStatus.Loaded; }
        }

        public static SettingsLoadResult Create(SettingsLoadStatus status, Exception error, string message)
        {
            return new SettingsLoadResult
            {
                Status = status,
                Error = error,
                Message = message
            };
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Message)
                ? Status.ToString()
                : string.Format("{0}: {1}", Status, Message);
        }
    }
}
