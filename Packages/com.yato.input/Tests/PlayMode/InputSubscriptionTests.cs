using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace Yato.Input.Tests
{
    public sealed class InputSubscriptionTests
    {
        private InputContextService m_service;
        private InputActionAsset m_source;
        private InputSettings.BackgroundBehavior m_background;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode m_editorBehavior;
#endif
        [SetUp] public void Setup()
        {
            m_background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            m_editorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            m_source = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Gameplay");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            m_source.AddActionMap(map);
            m_service = new InputContextService(m_source);
        }
        [TearDown] public void Teardown()
        {
            m_service.Dispose(); Object.Destroy(m_source);
            InputSystem.settings.backgroundBehavior = m_background;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = m_editorBehavior;
#endif
        }
        [UnityTest] public IEnumerator InteractiveRebinding_ChangesOnlyRuntimeBindingAndRestoresAction()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                bool? completed = null;
                m_service.EnableContext("Gameplay");
                m_service.StartRebinding("Gameplay/Jump", 0, success => completed = success);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.J));
                InputSystem.Update();
                yield return new WaitForSecondsRealtime(0.2f);
                InputSystem.Update();
                Assert.AreEqual(true, completed);
                var action = m_service.Actions.FindAction("Gameplay/Jump");
                Assert.AreEqual("<Keyboard>/j", action.bindings[0].overridePath);
                Assert.IsTrue(action.enabled);
                Assert.IsNull(m_source.FindAction("Gameplay/Jump").bindings[0].overridePath);
            }
            finally { InputSystem.RemoveDevice(keyboard); }
        }

        [Test] public void Subscription_DisposesSymmetrically()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                int calls = 0;
                var subscription = m_service.Subscribe("Gameplay/Jump", _ => calls++);
                m_service.EnableContext("Gameplay");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                InputSystem.Update();
                Assert.AreEqual(1, calls);
                subscription.Dispose();
                subscription.Dispose();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                InputSystem.Update();
                Assert.AreEqual(1, calls);
            }
            finally { InputSystem.RemoveDevice(keyboard); }
        }

    }
}
