using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Yato.Core;

namespace Yato.UI.Input
{
    [CreateAssetMenu(menuName = "Yato/UI Input Profile")]
    public sealed class UIInputProfile : ScriptableObject, IProjectValidationSource
    {
        public InputActionAsset Actions;
        public string BaseMap = "Gameplay";
        public string UIMap = "UI";
        public UIInputBindings Bindings = new UIInputBindings();
        public IReadOnlyList<string> Validate() => ValidateAgainst(Actions);
        public IReadOnlyList<string> ValidateAgainst(InputActionAsset actions)
        {
            var errors = new List<string>();
            if (actions == null) { errors.Add("Input ActionAsset is missing"); return errors.AsReadOnly(); }
            if (string.IsNullOrWhiteSpace(BaseMap) || actions.FindActionMap(BaseMap) == null) errors.Add("Base input map not found: " + BaseMap);
            if (string.IsNullOrWhiteSpace(UIMap) || actions.FindActionMap(UIMap) == null) errors.Add("UI input map not found: " + UIMap);
            if (BaseMap == UIMap) errors.Add("Base and UI input maps must differ");
            if (Bindings == null) { errors.Add("UI input bindings are missing"); return errors.AsReadOnly(); }
            Check(actions, Bindings.Point, "Vector2", true, errors);
            Check(actions, Bindings.Click, "Button", true, errors);
            Check(actions, Bindings.Navigate, "Vector2", true, errors);
            Check(actions, Bindings.Submit, "Button", true, errors);
            Check(actions, Bindings.Cancel, "Button", true, errors);
            Check(actions, Bindings.Scroll, "Vector2", false, errors);
            return errors.AsReadOnly();
        }
        private void Check(InputActionAsset actions, string path, string control, bool required, List<string> errors)
        {
            if (!required && string.IsNullOrWhiteSpace(path)) return;
            var action = string.IsNullOrWhiteSpace(path) ? null : actions.FindAction(path);
            if (action == null) { errors.Add("UI action not found: " + path); return; }
            if (action.actionMap?.name != UIMap) errors.Add(path + ": action must belong to UI map " + UIMap);
            if (!string.IsNullOrEmpty(action.expectedControlType) && action.expectedControlType != control)
                errors.Add(path + ": expected " + control + ", got " + action.expectedControlType);
        }
    }
}
