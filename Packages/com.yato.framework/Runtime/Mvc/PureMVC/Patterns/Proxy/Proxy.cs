using PureMVC.Interfaces;
using PureMVC.Patterns.Observer;

namespace PureMVC.Patterns.Proxy
{
    /// <summary>数据代理默认实现。</summary>
    public class Proxy : Notifier, IProxy
    {
        public const string NAME = "Proxy";

        protected string m_proxyName;
        protected object m_data;

        public Proxy() : this(NAME, null)
        {
        }

        public Proxy(string proxyName) : this(proxyName, null)
        {
        }

        public Proxy(string proxyName, object data)
        {
            m_proxyName = proxyName ?? NAME;
            m_data = data;
        }

        public virtual string ProxyName
        {
            get { return m_proxyName; }
        }

        public virtual object Data
        {
            get { return m_data; }
            set { m_data = value; }
        }

        public virtual void OnRegister()
        {
        }

        public virtual void OnRemove()
        {
        }
    }
}
