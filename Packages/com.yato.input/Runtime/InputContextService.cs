using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RebindingOperation = UnityEngine.InputSystem.InputActionRebindingExtensions.RebindingOperation;

namespace Yato.Input
{
    /// <summary>Owns a runtime clone of the supplied actions. All operations must run on Unity's main thread.</summary>
    public sealed class InputContextService : IDisposable
    {
        private readonly List<Subscription> m_subscriptions = new List<Subscription>();
        private RebindingOperation m_rebind;
        private InputAction m_rebindingAction;
        private bool m_restoreEnabled;
        private bool m_disposed;
        public InputActionAsset Actions { get; private set; }

        public InputContextService(InputActionAsset actions)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            Actions = UnityEngine.Object.Instantiate(actions);
            Actions.Disable();
        }

        /// <summary>Enable a named context; exclusive switching disables all other maps.</summary>
        public void EnableContext(string mapName, bool exclusive = true)
        {
            EnsureAlive();
            var map = Actions.FindActionMap(mapName, true); // Validate before changing the current context.
            CancelRebinding();
            if (exclusive) Actions.Disable();
            map.Enable();
        }

        public void DisableAll()
        {
            EnsureAlive();
            CancelRebinding();
            Actions.Disable();
        }

        public IDisposable Subscribe(string actionPath, Action<InputAction.CallbackContext> callback,
            InputActionPhase phase = InputActionPhase.Performed)
        {
            EnsureAlive();
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var action = Actions.FindAction(actionPath, true);
            var subscription = new Subscription(this, action, callback, phase);
            m_subscriptions.Add(subscription);
            return subscription;
        }

        /// <summary>One binding at a time. Composite roots must be rebound using their individual parts.</summary>
        public void StartRebinding(string actionPath, int bindingIndex, Action<bool> completed = null,
            string cancelControl = "<Keyboard>/escape")
        {
            EnsureAlive();
            var action = Actions.FindAction(actionPath, true);
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count || action.bindings[bindingIndex].isComposite)
                throw new ArgumentOutOfRangeException(nameof(bindingIndex));
            CancelRebinding();
            m_rebindingAction = action;
            m_restoreEnabled = action.enabled;
            action.Disable();
            try
            {
                m_rebind = action.PerformInteractiveRebinding(bindingIndex)
                    .WithCancelingThrough(cancelControl)
                    .OnCancel(operation => FinishRebinding(operation, false, completed))
                    .OnComplete(operation => FinishRebinding(operation, true, completed));
                m_rebind.Start();
            }
            catch
            {
                var operation = m_rebind;
                m_rebind = null;
                operation?.Dispose();
                m_rebindingAction = null;
                if (m_restoreEnabled && !m_disposed) action.Enable();
                throw;
            }
        }

        public void CancelRebinding()
        {
            m_rebind?.Cancel(); // Official callback restores the action and disposes the operation.
        }

        private void FinishRebinding(RebindingOperation operation, bool success, Action<bool> completed)
        {
            var action = m_rebindingAction;
            bool restore = m_restoreEnabled && !m_disposed;
            m_rebind = null;
            m_rebindingAction = null;
            operation.Dispose();
            if (restore) action.Enable();
            completed?.Invoke(success);
        }

        public string SaveBindingOverrides()
        {
            EnsureAlive();
            return Actions.SaveBindingOverridesAsJson();
        }

        public void LoadBindingOverrides(string json)
        {
            EnsureAlive();
            CancelRebinding();
            Actions.LoadBindingOverridesFromJson(json);
        }

        public void ResetBindingOverrides()
        {
            EnsureAlive();
            CancelRebinding();
            Actions.RemoveAllBindingOverrides();
        }

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            try { CancelRebinding(); }
            finally
            {
                foreach (var subscription in m_subscriptions.ToArray()) subscription.Dispose();
                Actions.Disable();
                if (Application.isPlaying) UnityEngine.Object.Destroy(Actions);
                else UnityEngine.Object.DestroyImmediate(Actions);
                Actions = null;
            }
        }

        private void EnsureAlive()
        {
            if (m_disposed) throw new ObjectDisposedException(nameof(InputContextService));
        }

        private sealed class Subscription : IDisposable
        {
            private InputContextService m_owner;
            private readonly InputAction m_action;
            private readonly Action<InputAction.CallbackContext> m_callback;
            private readonly InputActionPhase m_phase;
            public Subscription(InputContextService owner, InputAction action, Action<InputAction.CallbackContext> callback, InputActionPhase phase)
            {
                m_owner = owner; m_action = action; m_callback = callback; m_phase = phase;
                switch (phase)
                {
                    case InputActionPhase.Started: action.started += callback; break;
                    case InputActionPhase.Performed: action.performed += callback; break;
                    case InputActionPhase.Canceled: action.canceled += callback; break;
                    default: throw new ArgumentOutOfRangeException(nameof(phase));
                }
            }
            public void Dispose()
            {
                if (m_owner == null) return;
                switch (m_phase)
                {
                    case InputActionPhase.Started: m_action.started -= m_callback; break;
                    case InputActionPhase.Performed: m_action.performed -= m_callback; break;
                    case InputActionPhase.Canceled: m_action.canceled -= m_callback; break;
                }
                m_owner.m_subscriptions.Remove(this);
                m_owner = null;
            }
        }
    }
}
