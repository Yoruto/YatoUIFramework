using System;

namespace Yato.Core
{
    /// <summary>
    /// 框架统一日志入口。默认使用 <see cref="UnityLogger"/>，
    /// 业务可在启动时通过 <see cref="SetLogger"/> 替换实现。
    /// </summary>
    public static class Log
    {
        private static ILogger s_logger = new UnityLogger();

        /// <summary>当前日志实现；赋 null 会回退到默认实现。</summary>
        public static ILogger Logger
        {
            get { return s_logger; }
        }

        public static void SetLogger(ILogger logger)
        {
            s_logger = logger ?? new UnityLogger();
        }

        public static void Debug(string message)
        {
            s_logger.Debug(message);
        }

        public static void Info(string message)
        {
            s_logger.Info(message);
        }

        public static void Warn(string message)
        {
            s_logger.Warn(message);
        }

        public static void Error(string message, Exception exception = null)
        {
            s_logger.Error(message, exception);
        }

        /// <summary>带上下文前缀的错误日志，便于定位页面 / 资源 key。</summary>
        public static void Error(string context, string message, Exception exception = null)
        {
            s_logger.Error(string.Format("[{0}] {1}", context, message), exception);
        }
    }
}
