using System;
using System.Collections.Generic;
using PureMVC.Interfaces;
using PureMVC.Patterns.Observer;

namespace PureMVC.Patterns.Command
{
    /// <summary>命令组合：按注册顺序依次执行子命令。</summary>
    public class MacroCommand : Notifier, ICommand
    {
        private readonly List<Func<ICommand>> m_subCommands = new List<Func<ICommand>>(4);

        public MacroCommand()
        {
            InitializeMacroCommand();
        }

        /// <summary>子类在此注册子命令。</summary>
        protected virtual void InitializeMacroCommand()
        {
        }

        protected void AddSubCommand(Func<ICommand> factory)
        {
            if (factory != null)
            {
                m_subCommands.Add(factory);
            }
        }

        public virtual void Execute(INotification notification)
        {
            for (int i = 0; i < m_subCommands.Count; i++)
            {
                ICommand command = m_subCommands[i]();
                if (command != null)
                {
                    command.Execute(notification);
                }
            }
        }
    }
}
