using UnityEditor;
using UnityEngine;
using Yato.Core;
using Yato.Resources;

namespace Yato.Editor
{
    public sealed class FrameworkDiagnosticsWindow : EditorWindow
    {
        private Vector2 m_scroll;
        private double m_nextRefresh;
        private string m_filter = "";
        [MenuItem("Yato/Tools/Framework Diagnostics", false, 399)]
        public static void Open()
        {
            var window = GetWindow<FrameworkDiagnosticsWindow>("Yato Diagnostics");
            window.minSize = new Vector2(500, 320); window.Show();
        }
        private void OnEnable() { EditorApplication.update += Tick; }
        private void OnDisable() { EditorApplication.update -= Tick; }
        private void Tick()
        {
            if (EditorApplication.timeSinceStartup < m_nextRefresh) return;
            m_nextRefresh = EditorApplication.timeSinceStartup + 0.25;
            Repaint();
        }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Live read-only snapshots. Optional modules appear when instantiated. Capture allocates; no runtime hot-path polling.", MessageType.Info);
            m_filter = EditorGUILayout.TextField("Filter", m_filter);
            var resources = ResourceServiceFactory.Current;
            EditorGUILayout.LabelField("Resource handles", ResourceTracker.Count.ToString());
            if (resources?.Stats != null)
                EditorGUILayout.LabelField("Resources", $"assets={resources.Stats.ActiveAssetCount} bundles={resources.Stats.LoadedBundleCount}");
            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            foreach (var snapshot in DiagnosticRegistry.Capture())
            {
                if (!string.IsNullOrEmpty(m_filter) &&
                    (snapshot.Module + " " + snapshot.Name).IndexOf(m_filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(snapshot.Module + " / " + snapshot.Name, snapshot.State, EditorStyles.boldLabel);
                foreach (var value in snapshot.Values) EditorGUILayout.LabelField(value.Key, value.Value);
                if (!string.IsNullOrEmpty(snapshot.LastError)) EditorGUILayout.HelpBox(snapshot.LastError, MessageType.Warning);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
