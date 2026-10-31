using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Yato.App
{
    /// <summary>Explicit cooperative exit. Does not promise async saving from OS kill/OnApplicationQuit.</summary>
    public sealed class ApplicationExitCoordinator : IDisposable
    {
        private readonly List<Func<CancellationToken, Task>> m_steps = new List<Func<CancellationToken, Task>>();
        private readonly Action m_quit;
        private readonly CancellationTokenSource m_lifetime = new CancellationTokenSource();
        private bool m_disposed;
        public bool IsBusy { get; private set; }
        public ApplicationExitCoordinator(Action quit = null) { m_quit = quit ?? (() => Application.Quit()); }
        public IDisposable Register(Func<CancellationToken, Task> step)
        {
            if (m_disposed) throw new ObjectDisposedException(nameof(ApplicationExitCoordinator));
            if (step == null) throw new ArgumentNullException(nameof(step));
            m_steps.Add(step);
            return new Registration(this, step);
        }
        public async Task<bool> RequestExitAsync(CancellationToken ct = default)
        {
            if (m_disposed) throw new ObjectDisposedException(nameof(ApplicationExitCoordinator));
            if (IsBusy) return false;
            IsBusy = true;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_lifetime.Token))
                {
                    foreach (var step in m_steps.ToArray())
                    { linked.Token.ThrowIfCancellationRequested(); await step(linked.Token); }
                    linked.Token.ThrowIfCancellationRequested();
                    GameBootstrap.Shutdown();
                    m_quit();
                    return true;
                }
            }
            finally { IsBusy = false; }
        }
        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true; m_steps.Clear(); m_lifetime.Cancel(); m_lifetime.Dispose();
        }
        private sealed class Registration : IDisposable
        {
            private ApplicationExitCoordinator m_owner;
            private readonly Func<CancellationToken, Task> m_step;
            public Registration(ApplicationExitCoordinator owner, Func<CancellationToken, Task> step) { m_owner = owner; m_step = step; }
            public void Dispose() { m_owner?.m_steps.Remove(m_step); m_owner = null; }
        }
    }
}
