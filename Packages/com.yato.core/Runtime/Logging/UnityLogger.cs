using System;
using UnityEngine;

namespace Yato.Core
{
    /// <summary>基于 Unity 控制台的默认日志实现。</summary>
    public sealed class UnityLogger : ILogger
    {
        private readonly string m_tag;

        public UnityLogger(string tag = "Yato")
        {
            m_tag = string.IsNullOrEmpty(tag) ? "Yato" : tag;
        }

#if UNITY_EDITOR || YATO_VERBOSE
        /// <summary>是否输出 Debug 级日志。</summary>
        public static bool IsDebugEnabled
        {
            get { return true; }
        }
#else
        /// <summary>是否输出 Debug 级日志。</summary>
        public static bool IsDebugEnabled
        {
            get { return false; }
        }
#endif

        public void Debug(string message)
        {
            if (!IsDebugEnabled)
            {
                return;
            }

            UnityEngine.Debug.Log(Format(message));
        }

        public void Info(string message)
        {
            UnityEngine.Debug.Log(Format(message));
        }

        public void Warn(string message)
        {
            UnityEngine.Debug.LogWarning(Format(message));
        }

        public void Error(string message, Exception exception = null)
        {
            UnityEngine.Debug.LogError(Format(message));
            if (exception != null)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        private string Format(string message)
        {
            return string.IsNullOrEmpty(message)
                ? string.Format("[{0}] (empty message)", m_tag)
                : string.Format("[{0}] {1}", m_tag, message);
        }
    }
}
