// Copied to an isolated consumer's Assets/Editor; never imports the source project's Assets.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Yato
{
    public static partial class ModularConsumerVerification
    {
        internal const string Key = "yato_modular_fixture";
        internal const string PrefabPath = "Assets/Resources/" + Key + ".prefab";

        [Serializable]
        private sealed class Preparation
        {
            public string packageSet;
            public string[] yatoPackages;
            public bool includeBundles, includeHotUpdate;
        }

        [Serializable]
        private sealed class Verification
        {
            public string packageSet, unityVersion;
            public string[] expectedPackages, registeredPackages;
            public bool exactPackageSet, resourceFixturePrepared, bundleFixturePrepared;
        }

        public static void PrepareAndVerify()
        {
            var preparation = JsonUtility.FromJson<Preparation>(File.ReadAllText("modular-preparation.json"));
            string[] actual = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Select(package => package.name)
                .Where(name => name.StartsWith("com.yato.", StringComparison.Ordinal)).OrderBy(name => name).ToArray();
            string[] expected = preparation.yatoPackages.OrderBy(name => name).ToArray();
            if (!actual.SequenceEqual(expected))
                throw new InvalidOperationException("Yato package set differs from preparation. Expected: " +
                    string.Join(", ", expected) + "; registered: " + string.Join(", ", actual));

            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.Refresh();
            var prefab = new GameObject(Key);
            try
            {
                AddOptionalUI(prefab);
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(prefab); }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            PrepareOptionalBundles();

            var report = new Verification
            {
                packageSet = preparation.packageSet,
                unityVersion = Application.unityVersion,
                expectedPackages = expected,
                registeredPackages = actual,
                exactPackageSet = true,
                resourceFixturePrepared = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null,
                bundleFixturePrepared = preparation.includeBundles
            };
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/Modular-Verify.json", JsonUtility.ToJson(report, true));
            Debug.Log("[Yato.ModularConsumer] Prepared " + preparation.packageSet + ": " +
                string.Join(", ", actual) + ". Run Yato.Modular.Consumer.Tests for runtime validation.");
        }

        public static void BuildResourcesPlayer()
        {
            var preparation = JsonUtility.FromJson<Preparation>(File.ReadAllText("modular-preparation.json"));
            if (!preparation.yatoPackages.OrderBy(name => name).SequenceEqual(new[] { "com.yato.core", "com.yato.resources" }))
                throw new InvalidOperationException("Resources Player smoke requires the exact Core + Resources package set");
            if (!File.Exists("Assets/ModularResourcesPlayerSmoke.cs"))
                throw new InvalidOperationException("Missing ResourcesOnly Player smoke script");
            PrepareAndVerify();
            const string scenePath = "Assets/ModularResourcesOnly.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new InvalidOperationException("Could not save the ResourcesOnly build scene");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            string output = Path.GetFullPath("Build/ResourcesOnly.exe");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("ResourcesOnly build failed: " + report.summary.result);
            string[] assemblies = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(output), "ResourcesOnly_Data/Managed"), "Yato.*.dll")
                .Select(Path.GetFileNameWithoutExtension).OrderBy(name => name).ToArray();
            string[] expected = { "Yato.Core", "Yato.Resources", "Yato.Resources.Abstractions" };
            if (!assemblies.SequenceEqual(expected))
                throw new InvalidOperationException("ResourcesOnly Player has unexpected Yato assemblies: " + string.Join(", ", assemblies));
            Debug.Log("[Yato.ModularConsumer] Windows Mono built: " + output + "; Yato assemblies: " + string.Join(", ", assemblies));
        }

        static partial void AddOptionalUI(GameObject prefab);
        static partial void PrepareOptionalBundles();
    }
}
