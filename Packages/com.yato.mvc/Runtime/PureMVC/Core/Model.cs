using System;
using System.Collections.Generic;
using PureMVC.Interfaces;

namespace PureMVC.Core
{
    /// <summary>Proxy 注册表。</summary>
    public class Model : IModel
    {
        private static IModel s_instance;

        protected readonly Dictionary<string, IProxy> m_proxyMap = new Dictionary<string, IProxy>(StringComparer.Ordinal);

        internal Model()
        {
        }

        public static IModel GetInstance(Func<IModel> factory = null)
        {
            if (s_instance == null)
            {
                s_instance = factory != null ? factory() : new Model();
                if (s_instance == null)
                {
                    s_instance = new Model();
                }
            }

            return s_instance;
        }

        /// <summary>供 Facade.ResetAll 使用，避免关闭域重载时的脏状态。</summary>
        public static void ResetInstance()
        {
            s_instance = null;
        }

        public virtual bool HasProxy(string proxyName)
        {
            return !string.IsNullOrEmpty(proxyName) && m_proxyMap.ContainsKey(proxyName);
        }

        public virtual void RegisterProxy(IProxy proxy)
        {
            if (proxy == null || string.IsNullOrEmpty(proxy.ProxyName))
            {
                return;
            }

            m_proxyMap[proxy.ProxyName] = proxy;
            proxy.OnRegister();
        }

        public virtual IProxy RetrieveProxy(string proxyName)
        {
            if (string.IsNullOrEmpty(proxyName))
            {
                return null;
            }

            IProxy proxy;
            return m_proxyMap.TryGetValue(proxyName, out proxy) ? proxy : null;
        }

        public virtual IProxy RemoveProxy(string proxyName)
        {
            if (string.IsNullOrEmpty(proxyName))
            {
                return null;
            }

            IProxy proxy;
            if (!m_proxyMap.TryGetValue(proxyName, out proxy))
            {
                return null;
            }

            m_proxyMap.Remove(proxyName);
            proxy.OnRemove();
            return proxy;
        }
    }
}
