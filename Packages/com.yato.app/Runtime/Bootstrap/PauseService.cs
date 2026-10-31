using System;
using System.Collections.Generic;
using UnityEngine;
using Yato.Core;

namespace Yato.App
{
    /// <summary>Main-thread nested pause tokens. One service should own Time.timeScale for the application.</summary>
    public sealed class PauseService : IDisposable
    {
        private readonly Dictionary<int, string> m_tokens = new Dictionary<int, string>();
        private int m_sequence;
        private float m_resumeScale;
        private bool m_disposed;
        private IDisposable m_focusPause;
        public bool IsPaused => m_tokens.Count > 0;
        public int PauseCount => m_tokens.Count;
        public event Action<bool> Changed;
        public PauseService(bool pauseOnFocusLoss = false)
        {
            if (pauseOnFocusLoss)
            {
                ApplicationLifecycle.FocusChanged += OnFocus;
                if (!ApplicationLifecycle.IsFocused) OnFocus(false);
            }
        }
        private void OnFocus(bool focused)
        {
            if (!focused && m_focusPause == null) m_focusPause = Acquire("focus");
            if (focused) { m_focusPause?.Dispose(); m_focusPause = null; }
        }
        public IDisposable Acquire(string reason)
        {
            if (m_disposed) throw new ObjectDisposedException(nameof(PauseService));
            if (m_tokens.Count == 0) { m_resumeScale = Time.timeScale; Time.timeScale = 0; }
            int id = ++m_sequence;
            m_tokens.Add(id, reason ?? string.Empty);
            if (m_tokens.Count == 1) Raise(true);
            return new Token(this, id);
        }
        private void Release(int id)
        {
            if (!m_tokens.Remove(id) || m_tokens.Count != 0) return;
            Time.timeScale = m_resumeScale; Raise(false);
        }
        private void Raise(bool value)
        {
            if (Changed == null) return;
            foreach (Action<bool> callback in Changed.GetInvocationList())
                try { callback(value); } catch (Exception error) { Log.Error("Pause", "Observer failed", error); }
        }
        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            ApplicationLifecycle.FocusChanged -= OnFocus;
            m_focusPause?.Dispose(); m_focusPause = null;
            if (m_tokens.Count > 0) { m_tokens.Clear(); Time.timeScale = m_resumeScale; Raise(false); }
            Changed = null;
        }
        private sealed class Token : IDisposable
        {
            private PauseService m_owner; private readonly int m_id;
            public Token(PauseService owner, int id) { m_owner = owner; m_id = id; }
            public void Dispose() { var owner = m_owner; m_owner = null; owner?.Release(m_id); }
        }
    }
}
