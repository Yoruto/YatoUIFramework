using PureMVC.Interfaces;

namespace PureMVC.Patterns.Observer
{
    /// <summary>通知的默认实现。</summary>
    public class Notification : INotification
    {
        private readonly string m_name;
        private object m_body;
        private string m_type;

        public Notification(string name) : this(name, null, null)
        {
        }

        public Notification(string name, object body) : this(name, body, null)
        {
        }

        public Notification(string name, object body, string type)
        {
            m_name = name;
            m_body = body;
            m_type = type;
        }

        public string Name
        {
            get { return m_name; }
        }

        public object Body
        {
            get { return m_body; }
            set { m_body = value; }
        }

        public string Type
        {
            get { return m_type; }
            set { m_type = value; }
        }

        public override string ToString()
        {
            return string.Format("Notification Name:{0} Body:{1} Type:{2}", m_name, m_body, m_type);
        }
    }
}
