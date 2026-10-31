using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yato.Core
{
    /// <summary>默认服务定位实现，按类型索引。</summary>
    public sealed class ServiceLocator : Yato.Core.IServiceProvider
    {
        private readonly Dictionary<Type, object> m_services = new Dictionary<Type, object>();
        private readonly object m_gate = new object();

        private static ServiceLocator s_default;

        /// <summary>全局默认容器。</summary>
        public static ServiceLocator Default
        {
            get
            {
                if (s_default == null)
                {
                    s_default = new ServiceLocator();
                }

                return s_default;
            }
        }

        /// <summary>关闭域重载重复进入 Play 时清空，避免拿到上一次会话的实例。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayMode()
        {
            s_default = null;
        }

        public void Register<T>(T service)
        {
            Register(typeof(T), service);
        }

        /// <summary>以指定类型注册（便于按接口注册具体实现）。</summary>
        public void Register(Type serviceType, object service)
        {
            if (serviceType == null)
            {
                throw new ArgumentNullException("serviceType");
            }

            if (service == null)
            {
                throw new ArgumentNullException("service");
            }

            if (!serviceType.IsInstanceOfType(service))
            {
                throw new ArgumentException(
                    string.Format("{0} 不是 {1} 的实例", service.GetType().FullName, serviceType.FullName),
                    "service");
            }

            lock (m_gate)
            {
                m_services[serviceType] = service;
            }
        }

        public T Resolve<T>()
        {
            T service;
            if (TryResolve(out service))
            {
                return service;
            }

            throw new InvalidOperationException(
                string.Format("[Yato.ServiceLocator] 服务未注册：{0}", typeof(T).FullName));
        }

        public bool TryResolve<T>(out T service)
        {
            object boxed;
            lock (m_gate)
            {
                if (!m_services.TryGetValue(typeof(T), out boxed))
                {
                    service = default(T);
                    return false;
                }
            }

            if (boxed is T)
            {
                service = (T)boxed;
                return true;
            }

            service = default(T);
            return false;
        }

        /// <summary>移除指定类型的注册，返回是否存在。</summary>
        public bool Unregister<T>()
        {
            lock (m_gate)
            {
                return m_services.Remove(typeof(T));
            }
        }

        /// <summary>清空全部注册（场景切换 / 测试收尾）。</summary>
        public void Clear()
        {
            lock (m_gate)
            {
                m_services.Clear();
            }
        }

        /// <summary>当前注册数量（诊断用）。</summary>
        public int Count
        {
            get
            {
                lock (m_gate)
                {
                    return m_services.Count;
                }
            }
        }
    }
}
