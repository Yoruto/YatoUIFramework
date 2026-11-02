using UnityEditor;
using UnityEngine;
namespace Yato.Survivor.Editor
{
    [InitializeOnLoad]
    public static class SurvivorEditorExit
    {
        static SurvivorEditorExit() { EditorApplication.playModeStateChanged += Changed; }
        private static void Changed(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            var game = Object.FindAnyObjectByType<SurvivorGame>();
            if (game != null) game.QuitRequested += () => EditorApplication.isPlaying = false;
        }
    }
}
