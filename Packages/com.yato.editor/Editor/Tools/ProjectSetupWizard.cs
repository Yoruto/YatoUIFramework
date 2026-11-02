using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Yato.App;
using Yato.Resources;
using Yato.UI;

namespace Yato.Editor
{
    public sealed class ProjectSetupWizard : EditorWindow
    {
        private string m_root = "Assets/YatoStarter";
        private bool m_addToBuild;
        [MenuItem("Yato/Setup/Create Starter", false, 100)]
        public static void Open()
        {
            var window = GetWindow<ProjectSetupWizard>("Yato Setup");
            window.minSize = new Vector2(520, 240); window.Show();
        }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Create a Resources-based Bootstrap scene, page catalog and neutral UI prefab in a NEW folder. Existing assets and open scenes are preserved. Save/Input remain optional.", MessageType.Info);
            m_root = EditorGUILayout.TextField("New folder", m_root);
            m_addToBuild = EditorGUILayout.Toggle("Append scene to build list", m_addToBuild);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Create starter"))
                    try { string scene = Create(m_root, m_addToBuild); Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(scene); }
                    catch (Exception error) { Debug.LogError("[Yato.Setup] " + error.Message); }
        }
        public static string Create(string folder, bool addToBuild = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Create the starter in Edit Mode");
            folder = folder?.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets/", StringComparison.Ordinal) ||
                folder.Split('/').Any(x => x == "." || x == ".." || x.Length == 0))
                throw new ArgumentException("Use a new folder below Assets");
            string absolute = Path.GetFullPath(folder);
            if (!absolute.StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Folder escapes Assets");
            if (Directory.Exists(absolute) || File.Exists(absolute)) throw new IOException("Folder already exists; existing assets are never overwritten");
            for (var parent = Directory.GetParent(absolute); parent != null && parent.Exists; parent = parent.Parent)
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folders are not supported by setup");
            var inputModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            int handling = ProjectDoctor.ActiveInputHandling();
            if (handling == 1 && inputModule == null) throw new InvalidOperationException("Input System is selected but its UI module is unavailable");

            Directory.CreateDirectory(absolute);
            GameObject prefabObject = null;
            GameObject bootstrapRoot = null;
            try
            {
                string id = Guid.NewGuid().ToString("N").Substring(0, 8);
                string key = "yato/starter/" + id + "/page";
                string prefabPath = folder + "/Resources/" + key + ".prefab";
                Directory.CreateDirectory(Path.GetDirectoryName(prefabPath));
                AssetDatabase.Refresh();
                prefabObject = new GameObject("StarterPage", typeof(RectTransform), typeof(Image), typeof(UIPageView), typeof(UIPageTransition));
                var rect = (RectTransform)prefabObject.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                prefabObject.GetComponent<Image>().color = new Color(0.08f, 0.12f, 0.18f, 1f);
                var close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
                close.transform.SetParent(prefabObject.transform, false);
                ((RectTransform)close.transform).sizeDelta = new Vector2(240, 70);
                var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
                label.transform.SetParent(close.transform, false);
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
                var text = label.GetComponent<Text>();
                text.text = "Close page"; text.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 24; text.alignment = TextAnchor.MiddleCenter; text.color = Color.black; text.raycastTarget = false;
                UnityEventTools.AddPersistentListener(close.GetComponent<Button>().onClick, prefabObject.GetComponent<UIPageView>().Close);
                prefabObject.AddComponent<UIPageFocus>().DefaultSelected = close;
                if (PrefabUtility.SaveAsPrefabAsset(prefabObject, prefabPath) == null) throw new IOException("Could not save starter prefab");
                DestroyImmediate(prefabObject); prefabObject = null;

                var catalog = CreateInstance<UIPageCatalog>();
                catalog.Pages = new[] { new UIPageRegistration { PageId = "Main", ResourceKey = key } };
                AssetDatabase.CreateAsset(catalog, folder + "/Pages.asset");
                var config = CreateInstance<AppBootstrapConfig>();
                AssetDatabase.CreateAsset(config, folder + "/AppBootstrapConfig.asset");
                // Build a reusable prefab, then a scene containing one prefab instance. This avoids
                // Unity's additive-scene restriction when the user's active scene is still Untitled.
                bootstrapRoot = new GameObject("Yato Starter");
                var bootstrap = bootstrapRoot.AddComponent<GameBootstrap>();
                var serialized = new SerializedObject(bootstrap);
                serialized.FindProperty("m_config").objectReferenceValue = config;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var resourceHost = bootstrapRoot.AddComponent<ResourcesServiceHost>();
                var starter = bootstrapRoot.AddComponent<UIStarter>(); starter.Catalog = catalog; starter.ResourceSource = resourceHost;
                var events = new GameObject("EventSystem", typeof(EventSystem));
                events.transform.SetParent(bootstrapRoot.transform, false);
                events.AddComponent(handling != 0 && inputModule != null ? inputModule : typeof(StandaloneInputModule));
                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.transform.SetParent(bootstrapRoot.transform, false);
                camera.tag = "MainCamera"; camera.transform.position = new Vector3(0, 0, -10);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.08f, 0.12f, 0.18f);
                camera.gameObject.AddComponent<AudioListener>();
                string rootPath = folder + "/BootstrapRoot.prefab";
                var rootAsset = PrefabUtility.SaveAsPrefabAsset(bootstrapRoot, rootPath);
                if (rootAsset == null) throw new IOException("Could not save bootstrap prefab");
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(rootAsset.transform, out string rootGuid, out long transformId);
                string scenePath = folder + "/Bootstrap.unity";
                File.WriteAllText(scenePath, SceneTemplate(rootGuid, transformId), new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);
                if (addToBuild) EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(scenePath, true) }).ToArray();
                AssetDatabase.SaveAssets();
                return scenePath;
            }
            catch
            {
                AssetDatabase.DeleteAsset(folder); // Only this call's new folder; pre-existing folders were rejected above.
                throw;
            }
            finally
            {
                if (prefabObject != null) DestroyImmediate(prefabObject);
                if (bootstrapRoot != null) DestroyImmediate(bootstrapRoot);
            }
        }
        private static string SceneTemplate(string prefabGuid, long transformId)
        {
            return "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!1001 &100100000\nPrefabInstance:\n  m_ObjectHideFlags: 0\n  serializedVersion: 2\n" +
                "  m_Modification:\n    serializedVersion: 3\n    m_TransformParent: {fileID: 0}\n    m_Modifications: []\n" +
                "    m_RemovedComponents: []\n    m_RemovedGameObjects: []\n    m_AddedGameObjects: []\n    m_AddedComponents: []\n" +
                "  m_SourcePrefab: {fileID: 100100000, guid: " + prefabGuid + ", type: 3}\n" +
                "--- !u!4 &100100001 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: " + transformId + ", guid: " + prefabGuid + ", type: 3}\n" +
                "  m_PrefabInstance: {fileID: 100100000}\n  m_PrefabAsset: {fileID: 0}\n" +
                "--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_ObjectHideFlags: 0\n  m_Roots:\n  - {fileID: 100100001}\n";
        }
    }
}
