using UnityEditor;
using UnityEngine;
using Yato.Resources;

namespace Yato.Editor
{
    /// <summary>资源诊断窗口：常驻 bundle、引用计数、加载耗时（T-P4-05）。</summary>
    public sealed class ResourceWindow : EditorWindow
    {
        private Vector2 m_scroll;
        private bool m_autoRefresh = true;

        [MenuItem("Yato/Tools/Resource Window", false, 400)]
        public static void Open()
        {
            var window = GetWindow<ResourceWindow>("Yato Resources");
            window.minSize = new Vector2(420f, 260f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (m_autoRefresh && Application.isPlaying)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            IResourceService service = ResourceServiceFactory.Current;
            if (service == null)
            {
                EditorGUILayout.HelpBox("尚未创建 IResourceService（示例 GameStart 中调用 ResourceServiceFactory.Create）", MessageType.Info);
                return;
            }

            ResourceStats stats = service.Stats;
            if (stats == null)
            {
                EditorGUILayout.HelpBox("Provider 未提供统计", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("已加载 bundle", stats.LoadedBundleCount.ToString());
            EditorGUILayout.LabelField("活跃资产", stats.ActiveAssetCount.ToString());
            EditorGUILayout.LabelField("bundle 总大小", string.Format("{0:F2} MB", stats.TotalBundleBytes / 1024f / 1024f));
            EditorGUILayout.LabelField("未释放句柄", ResourceTracker.Count.ToString());

            m_autoRefresh = EditorGUILayout.Toggle("自动刷新", m_autoRefresh);
            EditorGUILayout.Space();

            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            if (stats.Bundles != null)
            {
                for (int i = 0; i < stats.Bundles.Count; i++)
                {
                    ResourceStats.BundleStat bundle = stats.Bundles[i];
                    EditorGUILayout.LabelField(
                        bundle.Name,
                        string.Format("ref={0} size={1:F1}KB load={2:F1}ms live={3}",
                            bundle.RefCount,
                            bundle.SizeBytes / 1024f,
                            bundle.LoadSeconds * 1000f,
                            bundle.IsLoaded));
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
