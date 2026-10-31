using System;
using UnityEngine;
using Yato.Core;

namespace Yato.App
{
    [AddComponentMenu("")]
    public sealed class ApplicationLifecycle : MonoBehaviour
    {
        public static bool IsFocused { get; private set; } = true;
        public static bool IsApplicationPaused { get; private set; }
        public static event Action<bool> FocusChanged;
        public static event Action<bool> PauseChanged;
        public static event Action Quitting;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            IsFocused = Application.isFocused; IsApplicationPaused = false;
            FocusChanged = null; PauseChanged = null; Quitting = null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var host = RuntimeHost.Instance;
            if (host.GetComponent<ApplicationLifecycle>() == null) host.gameObject.AddComponent<ApplicationLifecycle>();
        }
        private void OnApplicationFocus(bool focused)
        {
            if (IsFocused == focused) return;
            IsFocused = focused; Raise(FocusChanged, focused);
        }
        private void OnApplicationPause(bool paused)
        {
            if (IsApplicationPaused == paused) return;
            IsApplicationPaused = paused; Raise(PauseChanged, paused);
        }
        private void OnApplicationQuit()
        {
            if (Quitting == null) return;
            foreach (Action callback in Quitting.GetInvocationList())
                try { callback(); } catch (Exception error) { Log.Error("App", "Quit observer failed", error); }
        }
        private static void Raise(Action<bool> handlers, bool value)
        {
            if (handlers == null) return;
            foreach (Action<bool> callback in handlers.GetInvocationList())
                try { callback(value); } catch (Exception error) { Log.Error("App", "Lifecycle observer failed", error); }
        }
    }
}
