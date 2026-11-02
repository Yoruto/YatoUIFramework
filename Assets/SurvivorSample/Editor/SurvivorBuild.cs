using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yato.Editor;

namespace Yato.Survivor.Editor
{
    public static class SurvivorBuild
    {
        [MenuItem("Yato/Survivor/Build Windows Game")]
        public static void BuildWindows()
        {
            ProjectDoctor.ValidateOrThrow();
            var preview = EditorSceneManager.OpenPreviewScene("Assets/SurvivorSample/Scenes/SurvivorBootstrap.unity");
            try
            {
                foreach (var root in preview.GetRootGameObjects())
                {
                    var entry = root.GetComponentInChildren<SurvivorGame>();
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.DataRoot))
                        throw new InvalidOperationException("Clear the verification DataRoot before building a distributable game");
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            var bundle = BundleBuilder.Build(BuildTarget.StandaloneWindows64);
            if (!bundle.Success) throw new InvalidOperationException(string.Join("\n", bundle.Errors));
            var company = PlayerSettings.companyName; var product = PlayerSettings.productName;
            var version = PlayerSettings.bundleVersion; var width = PlayerSettings.defaultScreenWidth; var height = PlayerSettings.defaultScreenHeight;
            var mode = PlayerSettings.fullScreenMode;
            var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            try
            {
                PlayerSettings.companyName = "Yato"; PlayerSettings.productName = "The Last Watch"; PlayerSettings.bundleVersion = "0.1.0";
                PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900; PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                Directory.CreateDirectory("Build/SurvivorWindows");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/SurvivorSample/Scenes/SurvivorBootstrap.unity", "Assets/SurvivorSample/Scenes/SurvivorArena.unity" },
                    target = BuildTarget.StandaloneWindows64,
                    locationPathName = "Build/SurvivorWindows/TheLastWatch.exe", options = BuildOptions.None
                });
                var summary = report.summary;
                File.WriteAllText("Logs/Survivor-Windows-Build.json", "{\"result\":\"" + summary.result + "\",\"errors\":" + summary.totalErrors + ",\"warnings\":" + summary.totalWarnings + ",\"bytes\":" + summary.totalSize + "}");
                if (summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Survivor Windows build failed: " + summary.result);
            }
            finally
            {
                PlayerSettings.companyName = company; PlayerSettings.productName = product; PlayerSettings.bundleVersion = version;
                PlayerSettings.defaultScreenWidth = width; PlayerSettings.defaultScreenHeight = height; PlayerSettings.fullScreenMode = mode;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, backend);
            }
        }
    }
}
