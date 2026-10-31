using PureMVC.Interfaces;
using PureMVC.Patterns.Observer;

namespace PureMVC.Patterns.Command
{
    /// <summary>单条业务命令。</summary>
    public class SimpleCommand : Notifier, ICommand
    {
        public virtual void Execute(INotification notification)
        {
        }
    }
}
