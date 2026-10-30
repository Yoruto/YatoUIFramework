using System;

namespace Yato.Core
{
    /// <summary>框架日志抽象。业务可替换为上报/文件日志实现。</summary>
    public interface ILogger
    {
        /// <summary>调试日志，仅在 <c>UNITY_EDITOR</c> 或 <c>YATO_VERBOSE</c> 下输出。</summary>
        void Debug(string message);

        void Info(string message);

        void Warn(string message);

        /// <summary>错误日志必须带上下文；<paramref name="exception"/> 可为空。</summary>
        void Error(string message, Exception exception = null);
    }
}
