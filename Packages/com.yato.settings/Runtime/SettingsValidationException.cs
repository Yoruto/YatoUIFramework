using System;

namespace Yato.Settings
{
    /// <summary>设置内容未通过游戏自定义校验。</summary>
    public sealed class SettingsValidationException : Exception
    {
        public SettingsValidationException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }
}
