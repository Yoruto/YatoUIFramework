using System;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Yato.Input;
using Yato.Resources;

namespace Yato.UI.Input.FocusSample
{
    public sealed class FocusSampleEntry : MonoBehaviour
    {
        private InputActionAsset m_source;
        private InputContextService m_input;
        private UIInputCoordinator m_ui;
        private GameObject m_template, m_ownedEvents;
        private readonly UIPageDef m_main = new UIPageDef("FocusMain", null, new AssetKey("focus/main"));
        private readonly UIPageDef m_popup = new UIPageDef("FocusPopup", null, new AssetKey("focus/popup"), UILayer.Popup);
        private void Start()
        {
            m_source = ScriptableObject.CreateInstance<InputActionAsset>();
            var gameplay = new InputActionMap("Gameplay"); gameplay.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick"); m_source.AddActionMap(gameplay);
            var ui = new InputActionMap("UI");
            ui.AddAction("Point", InputActionType.PassThrough, "<Mouse>/position", expectedControlLayout: "Vector2");
            ui.AddAction("Click", InputActionType.PassThrough, "<Mouse>/leftButton", expectedControlLayout: "Button");
            var navigate = ui.AddAction("Navigate", InputActionType.Value, expectedControlLayout: "Vector2");
            navigate.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            navigate.AddBinding("<Gamepad>/leftStick"); navigate.AddBinding("<Gamepad>/dpad");
            var submit = ui.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter"); submit.AddBinding("<Gamepad>/buttonSouth");
            var cancel = ui.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape"); cancel.AddBinding("<Gamepad>/buttonEast");
            m_source.AddActionMap(ui);
            m_input = new InputContextService(m_source);
            var events = EventSystem.current;
            if (events == null) { m_ownedEvents = new GameObject("Focus sample EventSystem", typeof(EventSystem)); events = m_ownedEvents.GetComponent<EventSystem>(); }
            m_template = BuildTemplate();
            UIRouter.Configure(new ResourceService(new TemplateProvider(m_template)));
            m_ui = new UIInputCoordinator(m_input, events, "Gameplay", "UI");
            Open(false);
        }
        public void Open(bool popup)
        {
            var opening = UIRouter.OpenAsync(popup ? m_popup : m_main, new FocusSampleArgs { Owner = this, Popup = popup });
            opening.Completed += () => { try { opening.GetResult(); } catch (Exception error) { Debug.LogException(error); } };
        }
        private GameObject BuildTemplate()
        {
            var panel = new GameObject("Focus sample panel", typeof(RectTransform)); panel.SetActive(false);
            var rect = panel.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(560, 360);
            panel.AddComponent<Image>().color = new Color(0.15f, 0.2f, 0.3f, 0.98f);
            MakeText(panel.transform, "Title", "UI focus sample", new Vector2(0, 120), new Vector2(500, 60));
            var open = MakeButton(panel.transform, "OpenPopup", "Open popup", new Vector2(0, 25));
            MakeButton(panel.transform, "Close", "Close / restore previous focus", new Vector2(0, -65));
            panel.AddComponent<UIPageFocus>().DefaultSelected = open;
            panel.AddComponent<UIPageTransition>();
            panel.AddComponent<FocusSampleView>();
            return panel;
        }
        private static GameObject MakeButton(Transform parent, string name, string label, Vector2 position)
        {
            var button = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(parent, false);
            var rect = button.GetComponent<RectTransform>(); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(460, 64);
            MakeText(button.transform, "Label", label, Vector2.zero, new Vector2(440, 60));
            return button;
        }
        private static void MakeText(Transform parent, string name, string value, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchoredPosition = position; rect.sizeDelta = size;
            var text = go.GetComponent<Text>(); text.text = value; text.fontSize = 24; text.alignment = TextAnchor.MiddleCenter;
            text.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.color = name == "Title" ? Color.white : Color.black; text.raycastTarget = false;
        }
        private void OnGUI()
        {
            if (m_ui != null && GUI.Button(new Rect(15, 15, 240, 35), "Reopen focus sample")) Open(false);
        }
        private void OnDestroy()
        {
            UIRouter.CancelOpen(m_main.PageId); UIRouter.CancelOpen(m_popup.PageId); UIRouter.CloseAll();
            m_ui?.Dispose(); m_input?.Dispose();
            if (m_template != null) Destroy(m_template);
            if (m_source != null) Destroy(m_source);
            if (m_ownedEvents != null) Destroy(m_ownedEvents);
        }
        private sealed class TemplateProvider : IResourceProvider
        {
            private readonly GameObject m_prefab;
            public TemplateProvider(GameObject prefab) { m_prefab = prefab; }
            public ResourceStats Stats { get; } = new ResourceStats();
            public IAwaitable<UnityEngine.Object> LoadAsync(AssetKey key, Type type, CancellationToken ct)
            { var source = new Yato.Resources.AwaitableCompletionSource<UnityEngine.Object>(); source.SetResult(m_prefab); return source; }
            public void Release(AssetKey key) { }
            public void ReleaseUnused() { }
        }
    }
    public sealed class FocusSampleArgs { public FocusSampleEntry Owner; public bool Popup; }
}
