using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Yato.Input.Tests
{
    public sealed class InputContextServiceTests
    {
        private InputActionAsset m_source;
        private InputContextService m_service;
        [SetUp] public void Setup()
        {
            m_source = ScriptableObject.CreateInstance<InputActionAsset>();
            var gameplay = new InputActionMap("Gameplay");
            gameplay.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            m_source.AddActionMap(gameplay);
            var ui = new InputActionMap("UI");
            ui.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            m_source.AddActionMap(ui);
            m_service = new InputContextService(m_source);
        }
        [TearDown] public void Teardown() { m_service.Dispose(); Object.DestroyImmediate(m_source); }

        [Test] public void Contexts_CloneAndSwitchWithoutMutatingSource()
        {
            m_service.EnableContext("Gameplay");
            Assert.IsTrue(m_service.Actions.FindActionMap("Gameplay").enabled);
            Assert.IsFalse(m_source.FindActionMap("Gameplay").enabled);
            m_service.EnableContext("UI");
            Assert.IsFalse(m_service.Actions.FindActionMap("Gameplay").enabled);
            Assert.IsTrue(m_service.Actions.FindActionMap("UI").enabled);
            Assert.Throws<System.ArgumentException>(() => m_service.EnableContext("Missing"));
            Assert.IsTrue(m_service.Actions.FindActionMap("UI").enabled);
            m_service.DisableAll();
            Assert.IsFalse(m_service.Actions.FindActionMap("UI").enabled);
        }

        [Test] public void Rebinding_CancelRestoresStateAndContextSwitchDisablesOldMap()
        {
            m_service.EnableContext("Gameplay");
            bool? completed = null;
            m_service.StartRebinding("Gameplay/Jump", 0, success => completed = success);
            Assert.IsFalse(m_service.Actions.FindAction("Gameplay/Jump").enabled);
            m_service.CancelRebinding();
            Assert.AreEqual(false, completed);
            Assert.IsTrue(m_service.Actions.FindAction("Gameplay/Jump").enabled);
            m_service.StartRebinding("Gameplay/Jump", 0);
            m_service.EnableContext("UI");
            Assert.IsFalse(m_service.Actions.FindActionMap("Gameplay").enabled);
            m_service.Dispose();
            m_service.Dispose();
            Assert.Throws<System.ObjectDisposedException>(() => m_service.EnableContext("UI"));
        }

        [Test] public void BindingOverrides_RoundTripAndReset()
        {
            var action = m_service.Actions.FindAction("Gameplay/Jump");
            action.ApplyBindingOverride(0, "<Keyboard>/j");
            string json = m_service.SaveBindingOverrides();
            m_service.ResetBindingOverrides();
            Assert.IsNull(action.bindings[0].overridePath);
            m_service.LoadBindingOverrides(json);
            Assert.AreEqual("<Keyboard>/j", action.bindings[0].overridePath);
            Assert.IsNull(m_source.FindAction("Gameplay/Jump").bindings[0].overridePath);
        }
    }
}
