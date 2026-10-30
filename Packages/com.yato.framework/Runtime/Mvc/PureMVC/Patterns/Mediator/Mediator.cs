using PureMVC.Interfaces;
using PureMVC.Patterns.Observer;

namespace PureMVC.Patterns.Mediator
{
    /// <summary>中介者默认实现：持有 ViewComponent，按通知名订阅。</summary>
    public class Mediator : Notifier, IMediator
    {
        public const string NAME = "Mediator";

        protected string m_mediatorName;
        protected object m_viewComponent;

        public Mediator() : this(NAME, null)
        {
        }

        public Mediator(string mediatorName) : this(mediatorName, null)
        {
        }

        public Mediator(string mediatorName, object viewComponent)
        {
            m_mediatorName = mediatorName ?? NAME;
            m_viewComponent = viewComponent;
        }

        public virtual string MediatorName
        {
            get { return m_mediatorName; }
        }

        public virtual object ViewComponent
        {
            get { return m_viewComponent; }
            set { m_viewComponent = value; }
        }

        public virtual string[] ListNotificationInterests()
        {
            return new string[0];
        }

        public virtual void HandleNotification(INotification notification)
        {
        }

        public virtual void OnRegister()
        {
        }

        public virtual void OnRemove()
        {
        }
    }
}
