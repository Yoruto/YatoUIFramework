using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Yato.Input;

namespace Yato.UI.Input
{
    [Serializable]
    public sealed class UIInputBindings
    {
        public string Point = "UI/Point";
        public string Click = "UI/Click";
        public string Navigate = "UI/Navigate";
        public string Submit = "UI/Submit";
        public string Cancel = "UI/Cancel";
        public string Scroll = "UI/Scroll";
    }

    /// <summary>Owns UI event-module configuration and routed focus, not the supplied input service or EventSystem.</summary>
    public sealed class UIInputCoordinator : IDisposable
    {
        private readonly InputContextService m_input;
        private readonly EventSystem m_events;
        private readonly string m_uiMap;
        private string m_baseMap;
        private InputSystemUIInputModule m_module;
        private bool m_ownedModule;
        private bool m_oldEnabled;
        private InputActionAsset m_oldAsset;
        private InputActionReference[] m_oldRefs;
        private readonly List<InputActionReference> m_refs = new List<InputActionReference>();
        private readonly List<BaseInputModule> m_disabledModules = new List<BaseInputModule>();
        private readonly List<string> m_originalMaps = new List<string>();
        private readonly Dictionary<UIPageHandle, GameObject> m_focus = new Dictionary<UIPageHandle, GameObject>();
        private IDisposable m_cancel;
        private GameObject m_originalSelection, m_beforeCapture;
        private UIPageHandle m_active;
        private UIPageTransition m_watchedTransition;
        private bool m_disposed;

        public UIInputCoordinator(InputContextService input, EventSystem events, UIInputProfile profile)
            : this(input, events, ValidateProfile(input, profile).BaseMap, profile.UIMap, profile.Bindings) { }
        private static UIInputProfile ValidateProfile(InputContextService input, UIInputProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var errors = profile.ValidateAgainst(input?.Actions);
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            return profile;
        }

        public UIInputCoordinator(InputContextService input, EventSystem events, string baseMap,
            string uiMap, UIInputBindings bindings = null)
        {
            m_input = input ?? throw new ArgumentNullException(nameof(input));
            m_events = events != null ? events : throw new ArgumentNullException(nameof(events));
            m_uiMap = uiMap;
            input.Actions.FindActionMap(baseMap, true);
            input.Actions.FindActionMap(uiMap, true);
            m_baseMap = baseMap;
            bindings = bindings ?? new UIInputBindings();
            m_originalSelection = events.currentSelectedGameObject;
            foreach (var map in input.Actions.actionMaps) if (map.enabled) m_originalMaps.Add(map.name);
            try
            {
                m_module = events.GetComponent<InputSystemUIInputModule>();
                m_ownedModule = m_module == null;
                if (m_ownedModule)
                {
                    m_module = events.gameObject.AddComponent<InputSystemUIInputModule>();
                    m_module.enabled = false; // Let Unity release automatically assigned defaults before we capture anything.
                }
                m_oldEnabled = m_module.enabled;
                m_oldAsset = m_module.actionsAsset;
                m_oldRefs = new[] { m_module.point, m_module.leftClick, m_module.move, m_module.submit,
                    m_module.cancel, m_module.scrollWheel, m_module.rightClick, m_module.middleClick,
                    m_module.trackedDevicePosition, m_module.trackedDeviceOrientation };
                foreach (var module in events.GetComponents<BaseInputModule>())
                    if (module != m_module && module.enabled) { module.enabled = false; m_disabledModules.Add(module); }
                ClearReferences(m_module);
                m_module.actionsAsset = input.Actions;
                m_module.enabled = false;
                m_module.point = Reference(bindings.Point, true);
                m_module.leftClick = Reference(bindings.Click, true);
                m_module.move = Reference(bindings.Navigate, true);
                m_module.submit = Reference(bindings.Submit, true);
                m_module.cancel = Reference(bindings.Cancel, true);
                m_module.scrollWheel = Reference(bindings.Scroll, false);
                m_module.rightClick = null; m_module.middleClick = null;
                m_module.enabled = true;
                m_cancel = input.Subscribe(bindings.Cancel, _ => Back());
                UIRouter.PageOpened += OnOpened;
                UIRouter.PageClosed += OnClosed;
                UIRouter.TopPageChanged += OnTop;
                Refresh(true);
            }
            catch { Dispose(); throw; }
        }
        private static void ClearReferences(InputSystemUIInputModule module)
        {
            module.point = null; module.leftClick = null; module.move = null; module.submit = null;
            module.cancel = null; module.scrollWheel = null; module.rightClick = null; module.middleClick = null;
            module.trackedDevicePosition = null; module.trackedDeviceOrientation = null;
        }
        private InputActionReference Reference(string path, bool required)
        {
            var action = string.IsNullOrEmpty(path) ? null : m_input.Actions.FindAction(path, required);
            if (action == null) { if (required) throw new ArgumentException("Missing UI action: " + path); return null; }
            var reference = InputActionReference.Create(action);
            m_refs.Add(reference);
            return reference;
        }
        public void SetBaseContext(string mapName)
        {
            if (m_disposed) throw new ObjectDisposedException(nameof(UIInputCoordinator));
            m_input.Actions.FindActionMap(mapName, true);
            m_baseMap = mapName; Refresh(true);
        }
        private void OnOpened(UIPageHandle page) { Refresh(false); }
        private void OnClosed(UIPageHandle page) { Refresh(false); m_focus.Remove(page); }
        private void OnTop(UIPageHandle page) { Refresh(false); }
        private void Refresh(bool force)
        {
            if (m_disposed || m_events == null || m_input.Actions == null) return;
            UIPageHandle next = null;
            foreach (var page in UIRouter.Stack.GetAllHandles())
            {
                var instance = page.Context.Instance;
                var policy = instance != null ? instance.GetComponent<UIPageFocus>() : null;
                if (policy != null && policy.CaptureInput &&
                    (next == null || (int)page.Context.Layer >= (int)next.Context.Layer)) next = page;
            }
            if (!force && ReferenceEquals(next, m_active)) return;
            if (m_active != null) m_focus[m_active] = m_events.currentSelectedGameObject;
            if (m_active == null && next != null) m_beforeCapture = m_events.currentSelectedGameObject;
            if (m_watchedTransition != null) m_watchedTransition.Changed -= SelectActive;
            m_active = next;
            m_watchedTransition = next?.Context.Instance != null ? next.Context.Instance.GetComponent<UIPageTransition>() : null;
            if (m_watchedTransition != null) m_watchedTransition.Changed += SelectActive;
            m_input.EnableContext(next != null ? m_uiMap : m_baseMap);
            if (next == null)
            {
                m_events.SetSelectedGameObject(Selectable(m_beforeCapture) ? m_beforeCapture : null);
                return;
            }
            SelectActive();
        }
        private void SelectActive()
        {
            var next = m_active;
            if (m_disposed || m_events == null || next == null || !next.IsValid || next.Context.Instance == null) return;
            var root = next.Context.Instance;
            var focus = root.GetComponent<UIPageFocus>();
            m_focus.TryGetValue(next, out var selected);
            if (!Selectable(selected)) selected = focus.DefaultSelected;
            if (!Selectable(selected))
            {
                selected = null;
                foreach (var candidate in root.GetComponentsInChildren<UnityEngine.UI.Selectable>(false))
                    if (candidate.IsInteractable()) { selected = candidate.gameObject; break; }
            }
            m_events.SetSelectedGameObject(selected);
        }
        private static bool Selectable(GameObject target)
        {
            if (target == null || !target.activeInHierarchy) return false;
            var selectable = target.GetComponent<UnityEngine.UI.Selectable>();
            return selectable != null && selectable.IsActive() && selectable.IsInteractable();
        }
        public bool Back()
        {
            if (m_disposed) return false;
            var page = m_active;
            if (page == null || !page.IsValid || page.Context.Instance == null) return false;
            var policy = page.Context.Instance.GetComponent<UIPageFocus>();
            if (policy == null || !policy.AllowBack) return false;
            UIRouter.CloseAsync(page);
            return true;
        }
        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            UIRouter.PageOpened -= OnOpened; UIRouter.PageClosed -= OnClosed; UIRouter.TopPageChanged -= OnTop;
            m_cancel?.Dispose(); m_cancel = null;
            if (m_watchedTransition != null) m_watchedTransition.Changed -= SelectActive;
            m_watchedTransition = null;
            if (m_module != null)
            {
                m_module.enabled = false;
                ClearReferences(m_module);
                if (m_oldRefs != null && !m_ownedModule)
                {
                    m_module.actionsAsset = m_oldAsset;
                    m_module.point = m_oldRefs[0]; m_module.leftClick = m_oldRefs[1]; m_module.move = m_oldRefs[2];
                    m_module.submit = m_oldRefs[3]; m_module.cancel = m_oldRefs[4]; m_module.scrollWheel = m_oldRefs[5];
                    m_module.rightClick = m_oldRefs[6]; m_module.middleClick = m_oldRefs[7];
                    m_module.trackedDevicePosition = m_oldRefs[8]; m_module.trackedDeviceOrientation = m_oldRefs[9];
                }
                if (m_ownedModule) { m_module.actionsAsset = null; Destroy(m_module); } else m_module.enabled = m_oldEnabled;
            }
            foreach (var module in m_disabledModules) if (module != null) module.enabled = true;
            foreach (var reference in m_refs) if (reference != null) Destroy(reference);
            m_refs.Clear(); m_focus.Clear();
            if (m_input.Actions != null)
            {
                m_input.Actions.Disable();
                foreach (string name in m_originalMaps) m_input.Actions.FindActionMap(name, false)?.Enable();
            }
            if (m_events != null) m_events.SetSelectedGameObject(Selectable(m_originalSelection) ? m_originalSelection : null);
        }
        private static void Destroy(UnityEngine.Object value)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
