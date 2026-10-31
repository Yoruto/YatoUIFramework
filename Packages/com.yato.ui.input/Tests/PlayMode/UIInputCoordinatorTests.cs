using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Yato.Input;
using Yato.Resources;

namespace Yato.UI.Input.Tests
{
    public sealed class UIInputCoordinatorTests
    {
        private GameObject m_eventsObject, m_prefab;
        private EventSystem m_events;
        private InputActionAsset m_source;
        private InputContextService m_input;
        private UIInputCoordinator m_ui;
        private sealed class Provider : IResourceProvider
        {
            public GameObject Prefab;
            public ResourceStats Stats { get; } = new ResourceStats();
            public IAwaitable<UnityEngine.Object> LoadAsync(AssetKey key, Type type, CancellationToken ct)
            { var result = new Yato.Resources.AwaitableCompletionSource<UnityEngine.Object>(); result.SetResult(Prefab); return result; }
            public void Release(AssetKey key) { }
            public void ReleaseUnused() { }
        }
        [SetUp] public void Setup()
        {
            UIRouter.CloseAll();
            m_eventsObject = new GameObject("Input test events");
            m_events = m_eventsObject.AddComponent<EventSystem>();
            m_source = ScriptableObject.CreateInstance<InputActionAsset>();
            var gameplay = new InputActionMap("Gameplay"); gameplay.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick"); m_source.AddActionMap(gameplay);
            var ui = new InputActionMap("UI");
            ui.AddAction("Point", InputActionType.PassThrough, "<Mouse>/position", expectedControlLayout: "Vector2");
            ui.AddAction("Click", InputActionType.PassThrough, "<Mouse>/leftButton", expectedControlLayout: "Button");
            ui.AddAction("Navigate", InputActionType.Value, "<Gamepad>/leftStick", expectedControlLayout: "Vector2");
            ui.AddAction("Submit", InputActionType.Button, "<Gamepad>/buttonSouth");
            ui.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape");
            m_source.AddActionMap(ui);
            m_input = new InputContextService(m_source); m_input.EnableContext("Gameplay");
            m_prefab = new GameObject("Focus prefab", typeof(RectTransform)); m_prefab.SetActive(false);
            m_prefab.AddComponent<UIPageView>();
            var button = new GameObject("Default", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            button.transform.SetParent(m_prefab.transform, false);
            m_prefab.AddComponent<UIPageFocus>().DefaultSelected = button;
            UIRouter.Configure(new ResourceService(new Provider { Prefab = m_prefab }));
            m_ui = new UIInputCoordinator(m_input, m_events, "Gameplay", "UI");
        }
        [TearDown] public void Teardown()
        {
            m_ui.Dispose(); UIRouter.CloseAll(); m_input.Dispose();
            UnityEngine.Object.Destroy(m_source); UnityEngine.Object.Destroy(m_eventsObject); UnityEngine.Object.Destroy(m_prefab);
            UIRouter.Configure(NullResourceService.Instance);
        }
        [UnityTest] public IEnumerator NestedPages_RestoreFocusAndBaseMap()
        {
            var first = UIRouter.OpenAsync(new UIPageDef("One", null, new AssetKey("ui/one")));
            yield return first.AsEnumerator();
            GameObject selected = m_events.currentSelectedGameObject;
            Assert.IsNotNull(selected);
            Assert.IsTrue(m_input.Actions.FindActionMap("UI").enabled);
            Assert.IsFalse(m_input.Actions.FindActionMap("Gameplay").enabled);
            var second = UIRouter.OpenAsync(new UIPageDef("Two", null, new AssetKey("ui/two"), UILayer.Popup));
            yield return second.AsEnumerator();
            Assert.AreNotSame(selected, m_events.currentSelectedGameObject);
            second.GetResult().Close();
            Assert.AreSame(selected, m_events.currentSelectedGameObject);
            first.GetResult().Close();
            Assert.IsTrue(m_input.Actions.FindActionMap("Gameplay").enabled);
            Assert.IsFalse(m_input.Actions.FindActionMap("UI").enabled);
        }
        [UnityTest] public IEnumerator GamepadSubmit_UsesActualInputModuleAndSelectedButton()
        {
            var background = InputSystem.settings.backgroundBehavior;
            var editor = InputSystem.settings.editorInputBehaviorInPlayMode;
            var gamepad = InputSystem.AddDevice<Gamepad>();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            try
            {
                var loading = UIRouter.OpenAsync(new UIPageDef("Submit", null, new AssetKey("ui/submit")));
                yield return loading.AsEnumerator();
                int clicks = 0;
                m_events.currentSelectedGameObject.GetComponent<Button>().onClick.AddListener(() => clicks++);
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
                InputSystem.Update();
                m_eventsObject.GetComponent<InputSystemUIInputModule>().Process();
                Assert.AreEqual(1, clicks);
                InputSystem.QueueStateEvent(gamepad, new GamepadState()); InputSystem.Update();
            }
            finally
            {
                InputSystem.RemoveDevice(gamepad);
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.settings.editorInputBehaviorInPlayMode = editor;
            }
        }
        [UnityTest] public IEnumerator EnterTransition_RestoresSelectionWhenInteractionBecomesReady()
        {
            m_prefab.AddComponent<UIPageTransition>();
            var opening = UIRouter.OpenAsync(new UIPageDef("FadeFocus", null, new AssetKey("ui/fadefocus")));
            yield return opening.AsEnumerator();
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.IsNotNull(m_events.currentSelectedGameObject);
            Assert.IsTrue(m_events.currentSelectedGameObject.GetComponent<Button>().IsInteractable());
        }

        [UnityTest] public IEnumerator ExistingModule_DefaultActionsAreRestored()
        {
            m_ui.Dispose();
            yield return null;
            var existing = m_eventsObject.AddComponent<InputSystemUIInputModule>();
            var original = existing.actionsAsset;
            Assert.IsNotNull(original);
            m_ui = new UIInputCoordinator(m_input, m_events, "Gameplay", "UI");
            m_ui.Dispose();
            Assert.AreSame(original, existing.actionsAsset);
            Assert.IsTrue(existing.enabled);
            Assert.IsNotNull(existing.point.action.actionMap.asset);
        }

        [UnityTest] public IEnumerator Dispose_RestoresInputAndStopsReacting()
        {
            m_ui.Dispose();
            Assert.IsTrue(m_input.Actions.FindActionMap("Gameplay").enabled);
            var page = UIRouter.OpenAsync(new UIPageDef("Detached", null, new AssetKey("ui/detached")));
            yield return page.AsEnumerator();
            Assert.IsTrue(m_input.Actions.FindActionMap("Gameplay").enabled);
            Assert.IsFalse(m_ui.Back());
        }
    }
}
