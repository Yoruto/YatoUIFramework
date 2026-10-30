using System.Text;
using UnityEditor;
using UnityEngine;
using Yato.Resources;

namespace Yato.Editor
{
    /// <summary>
    /// 句柄泄漏报告（T-P4-06）：退出 Play 时汇总未释放句柄的创建堆栈。
    /// </summary>
    public sealed class LeakReportWindow : EditorWindow
    {
        private Vector2 m_scroll;

        [MenuItem("Yato/Tools/Leak Report", false, 401)]
        public static void Open()
        {
            var window = GetWindow<LeakReportWindow>("Yato Leak Report");
            window.minSize = new Vector2(480f, 300f);
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void HookPlayMode()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            var records = ResourceTracker.Snapshot();
            if (records.Count == 0)
            {
                ResourceTracker.Clear();
                return;
            }

            var builder = new StringBuilder();
            builder.AppendFormat("[Yato.Leak] 退出 Play 时仍有 {0} 个句柄未释放：\n", records.Count);
            for (int i = 0; i < records.Count && i < 20; i++)
            {
                builder.AppendFormat("  #{0} {1} @ {2}\n", records[i].Id, records[i].Key, records[i].CreatedAt);
            }

            Debug.LogWarning(builder.ToString());

            if (HasOpenInstances<LeakReportWindow>())
            {
                GetWindow<LeakReportWindow>().Repaint();
            }
        }

        private void OnGUI()
        {
            var records = ResourceTracker.Snapshot();
            EditorGUILayout.LabelField("未释放句柄", records.Count.ToString());

            if (GUILayout.Button("清空记录"))
            {
                ResourceTracker.Clear();
            }

            EditorGUILayout.Space();
            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            for (int i = 0; i < records.Count; i++)
            {
                EditorGUILayout.LabelField(string.Format("#{0} {1} @ {2}", records[i].Id, records[i].Key, records[i].CreatedAt));
                EditorGUILayout.TextArea(records[i].StackTrace, GUILayout.Height(64f));
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
