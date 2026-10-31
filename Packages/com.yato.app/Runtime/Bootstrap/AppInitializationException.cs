using System;

namespace Yato.App
{
    /// <summary>全局服务初始化失败；已完成的初始化已被回滚。</summary>
    public sealed class AppInitializationException : Exception
    {
        public AppInitializationException(string message, string serviceName, Exception innerException)
            : base(message, innerException)
        {
            ServiceName = serviceName;
        }

        /// <summary>失败的服务名；为 null 表示失败发生在注册/装配阶段。</summary>
        public string ServiceName { get; private set; }
    }
}
