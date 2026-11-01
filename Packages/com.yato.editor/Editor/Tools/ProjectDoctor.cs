using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yato.App;
using Yato.Resources;
using Yato.Core;
using Yato.UI;

namespace Yato.Editor
{
    public sealed class ProjectIssue
    {
        public string Path { get; }
        public string Message { get; }
        public bool IsError { get; }
        public ProjectIssue(string path, string message, bool error = true) { Path = path; Message = message; IsError = error; }
    }

    /// <summary>Read-only checks. Optional package assets participate via Core's validation contract.</summary>
    public sealed class ProjectDoctor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -90;
        public void OnPreprocessBuild(BuildReport report) => ValidateOrThrow();
        public static void ValidateOrThrow()
        {
            var errors = Check().Where(x => x.IsError).ToArray();
            if (errors.Length > 0) throw new BuildFailedException(string.Join("\n", errors.Select(x => x.Path + ": " + x.Message)));
        }
        public static IReadOnlyList<ProjectIssue> Check(string assetRoot = "Assets", bool checkBuildScenes = true)
        {
            if (!AssetDatabase.IsValidFolder(assetRoot)) throw new ArgumentException("Unknown asset folder", nameof(assetRoot));
            var issues = new List<ProjectIssue>();
            var resources = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { assetRoot }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x, StringComparer.Ordinal);
            foreach (string path in prefabs)
            {
                string normalized = path.Replace('\\', '/');
                int at = normalized.LastIndexOf("/Resources/", StringComparison.Ordinal);
                if (at >= 0)
                {
                    string key = normalized.Substring(at + 11);
                    key = key.Substring(0, key.Length - Path.GetExtension(key).Length);
                    if (!resources.TryGetValue(key, out var paths)) resources[key] = paths = new List<string>();
                    paths.Add(path);
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var binders = prefab.GetComponentsInChildren<ComponentBinder>(true);
                var focuses = prefab.GetComponentsInChildren<UIPageFocus>(true);
                if (prefab.GetComponent<IUIPage>() == null && binders.Length == 0 && focuses.Length == 0) continue;
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                        issues.Add(new ProjectIssue(path, "Missing script on " + transform.name));
                foreach (var binder in binders)
                    if (!binder.Validate(out string error)) issues.Add(new ProjectIssue(path, error));
                foreach (var focus in focuses)
                {
                    var selected = focus.DefaultSelected;
                    if (selected != null && (!selected.transform.IsChildOf(prefab.transform) || selected.GetComponent<Selectable>() == null))
                        issues.Add(new ProjectIssue(path, "Default selection must be a Selectable inside this page prefab"));
                    if (focus.CaptureInput && selected == null) issues.Add(new ProjectIssue(path, "Captured input has no default selection", false));
                }
            }
            // Find each concrete asset type once; missing optional packages do not add assembly references.
            var seen = new HashSet<string>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (type.IsAbstract || !typeof(IProjectValidationSource).IsAssignableFrom(type)) continue;
                foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name, new[] { assetRoot }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!seen.Add(path)) continue;
                    var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (!(asset is IProjectValidationSource validation)) continue;
                    try { foreach (string error in validation.Validate()) issues.Add(new ProjectIssue(path, error)); }
                    catch (Exception error) { issues.Add(new ProjectIssue(path, "Validator failed: " + error.Message)); }
                    if (!(asset is UIPageCatalog catalog) || !catalog.RequireResourcesPrefabs) continue;
                    foreach (var page in catalog.Pages ?? Array.Empty<UIPageRegistration>())
                    {
                        if (page == null || string.IsNullOrEmpty(page.ResourceKey)) continue;
                        if (!resources.TryGetValue(page.ResourceKey, out var paths) || paths.Count == 0)
                            issues.Add(new ProjectIssue(path, page.PageId + ": Resources prefab not found: " + page.ResourceKey));
                        else if (paths.Count > 1) issues.Add(new ProjectIssue(path, page.PageId + ": Ambiguous Resources key: " + string.Join(", ", paths)));
                        else if (AssetDatabase.LoadAssetAtPath<GameObject>(paths[0]).GetComponent<IUIPage>() == null)
                            issues.Add(new ProjectIssue(paths[0], page.PageId + ": Prefab root has no IUIPage"));
                    }
                }
            }
            if (checkBuildScenes)
                foreach (var scene in EditorBuildSettings.scenes.Where(x => x.enabled)) CheckScene(scene.path, issues);
            return issues.AsReadOnly();
        }
        private static void CheckScene(string path, List<ProjectIssue> issues)
        {
            if (!File.Exists(path)) { issues.Add(new ProjectIssue(path, "Build scene is missing")); return; }
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var roots = scene.GetRootGameObjects();
                var starters = roots.SelectMany(x => x.GetComponentsInChildren<UIStarter>(true)).ToArray();
                var events = roots.SelectMany(x => x.GetComponentsInChildren<EventSystem>(true)).ToArray();
                if (events.Length > 1) issues.Add(new ProjectIssue(path, "Multiple EventSystems in one scene"));
                if (roots.SelectMany(x => x.GetComponentsInChildren<GameBootstrap>(true)).Count() > 1)
                    issues.Add(new ProjectIssue(path, "Multiple GameBootstrap components"));
                foreach (var starter in starters)
                {
                    if (!(starter.ResourceSource is IResourceServiceSource)) issues.Add(new ProjectIssue(path, "UIStarter requires an IResourceServiceSource component"));
                    if (starter.Catalog == null) issues.Add(new ProjectIssue(path, "UIStarter has no page catalog"));
                    else try { starter.Catalog.Get(starter.InitialPageId); } catch (Exception error) { issues.Add(new ProjectIssue(path, error.Message)); }
                    if (events.Length == 0) issues.Add(new ProjectIssue(path, "UIStarter requires an EventSystem"));
                }
                int handling = ActiveInputHandling();
                foreach (var eventSystem in events)
                {
                    if (eventSystem.GetComponent<BaseInputModule>() == null) issues.Add(new ProjectIssue(path, "EventSystem has no input module"));
                    if (handling == 1 && eventSystem.GetComponent<StandaloneInputModule>() != null)
                        issues.Add(new ProjectIssue(path, "StandaloneInputModule requires Input Manager or Both"));
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        internal static int ActiveInputHandling()
        {
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            return settings.FindProperty("activeInputHandler")?.intValue ?? 0;
        }
    }

    public sealed class ProjectDoctorWindow : EditorWindow
    {
        private IReadOnlyList<ProjectIssue> m_issues;
        private Vector2 m_scroll;
        [MenuItem("Yato/Tools/Project Doctor", false, 398)]
        public static void Open()
        {
            var window = GetWindow<ProjectDoctorWindow>("Yato Project Doctor");
            window.minSize = new Vector2(540, 320); window.Show();
        }
        private void OnGUI()
        {
            if (GUILayout.Button("Check project and enabled build scenes")) m_issues = ProjectDoctor.Check();
            if (m_issues == null) return;
            EditorGUILayout.LabelField("Errors / warnings", m_issues.Count(x => x.IsError) + " / " + m_issues.Count(x => !x.IsError));
            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            foreach (var issue in m_issues)
            {
                EditorGUILayout.HelpBox(issue.Path + "\n" + issue.Message, issue.IsError ? MessageType.Error : MessageType.Warning);
                if (GUILayout.Button("Locate asset")) EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(issue.Path));
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
