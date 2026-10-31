// Local, manually invoked consumer validation. Copied only to a fresh test project's Assets/Editor.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yato.BasicSample;
using Yato.FullSample;

namespace Yato
{
    public static class ConsumerVerification
    {
        private const string Samples = "Assets/Samples/YatoFramework/";
        public static void Verify()
        {
            foreach (string name in new[] { "BasicSample/Scenes/YatoBasic.unity", "BasicSample/Scenes/YatoBasicSecond.unity", "FullSample/Scenes/YatoFull.unity" })
            {
                var scene = EditorSceneManager.OpenScene(Samples + name);
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                            throw new InvalidOperationException("Missing script: " + transform.name);
            }
            if (UnityEngine.Resources.Load<GameObject>("yato/basic/page")?.GetComponent<BasicPageView>() == null ||
                UnityEngine.Resources.Load<GameObject>("yato/full/page")?.GetComponent<FullPageView>() == null)
                throw new InvalidOperationException("Standalone sample prefab missing its view");
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(Samples + "BasicSample/Scenes/YatoBasic.unity", true),
                new EditorBuildSettingsScene(Samples + "BasicSample/Scenes/YatoBasicSecond.unity", true),
                new EditorBuildSettingsScene(Samples + "FullSample/Scenes/YatoFull.unity", true)
            };
            Debug.Log("[Yato.ConsumerVerification] PASS: standalone scenes and resource prefabs, no source Assets or ProjectSettings copied");
        }

        public static void BuildWindows()
        {
            Verify();
            string output = Path.GetFullPath("Build/YatoConsumer.exe");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Samples + "BasicSample/Scenes/YatoBasic.unity", Samples + "BasicSample/Scenes/YatoBasicSecond.unity", Samples + "FullSample/Scenes/YatoFull.unity" },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(report.summary.result.ToString());
            Debug.Log("[Yato.ConsumerVerification] Windows Player built: " + output);
        }
    }
}
