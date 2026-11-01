using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yato.UI.Input.Tests
{
    public sealed class UIInputProfileTests
    {
        [Test] public void InvalidMapsActionsAndCrossMapReferencesAreLocated()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var profile = ScriptableObject.CreateInstance<UIInputProfile>();
            try
            {
                var game = asset.AddActionMap("Gameplay");
                game.AddAction("Cancel", InputActionType.Button);
                var ui = asset.AddActionMap("UI");
                ui.AddAction("Point", InputActionType.PassThrough, expectedControlLayout: "Vector2");
                ui.AddAction("Click", InputActionType.PassThrough, expectedControlLayout: "Button");
                ui.AddAction("Navigate", InputActionType.PassThrough, expectedControlLayout: "Vector2");
                ui.AddAction("Submit", InputActionType.Button, expectedControlLayout: "Button");
                ui.AddAction("Cancel", InputActionType.Button, expectedControlLayout: "Button");
                profile.Actions = asset; profile.Bindings.Scroll = "";
                Assert.IsEmpty(profile.Validate());
                Assert.IsFalse(ui.enabled, "Preflight must not enable the source asset");
                profile.Bindings.Cancel = "Gameplay/Cancel";
                StringAssert.Contains("must belong to UI map", string.Join("\n", profile.Validate()));
                profile.Bindings.Submit = "UI/missing";
                StringAssert.Contains("UI/missing", string.Join("\n", profile.Validate()));
                profile.BaseMap = "missing";
                StringAssert.Contains("Base input map not found", string.Join("\n", profile.Validate()));
            }
            finally { Object.DestroyImmediate(profile); Object.DestroyImmediate(asset); }
        }
        [Test] public void MissingAssetIsReportedWithoutThrowing()
        {
            var profile = ScriptableObject.CreateInstance<UIInputProfile>();
            try { StringAssert.Contains("missing", string.Join("\n", profile.Validate())); }
            finally { Object.DestroyImmediate(profile); }
        }
    }
}
