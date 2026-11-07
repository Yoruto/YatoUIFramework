using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

namespace Yato.Project.Editor
{
    /// <summary>Source-project authoring only; distributed Yato packages do not depend on URP.</summary>
    public static class YatoRenderPipelineSetup
    {
        public const string RenderingRoot = "Assets/SurvivorSample/Rendering";
        private const string RendererPath = RenderingRoot + "/SurvivorRenderer2D.asset";
        private const string PipelinePath = RenderingRoot + "/SurvivorURP.asset";
        private const string GlobalPath = RenderingRoot + "/UniversalRenderPipelineGlobalSettings.asset";
        private const string ProfilePath = RenderingRoot + "/DefaultVolumeProfile.asset";

        [MenuItem("Yato/Project/Configure URP 2D")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before configuring the source render pipeline.");
            Directory.CreateDirectory(RenderingRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var renderer = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            var rendererSettings = new SerializedObject(renderer);
            rendererSettings.FindProperty("m_DefaultMaterialType").intValue = 1; // Renderer2DDefaultMaterialType.Unlit
            rendererSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            var pipelineSettings = new SerializedObject(pipeline);
            var renderers = pipelineSettings.FindProperty("m_RendererDataList");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            pipelineSettings.FindProperty("m_DefaultRendererIndex").intValue = 0;
            pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
            pipeline.supportsHDR = false;
            pipeline.msaaSampleCount = 1;
            pipeline.renderScale = 1f;
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int originalQuality = QualitySettings.GetQualityLevel();
            try
            {
                for (int index = 0; index < QualitySettings.names.Length; index++)
                {
                    QualitySettings.SetQualityLevel(index, false);
                    QualitySettings.renderPipeline = pipeline;
                }
            }
            finally { QualitySettings.SetQualityLevel(originalQuality, false); }

            ConfigureGlobalSettings();

            var unlit = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");
            if (unlit == null) throw new InvalidOperationException("URP Sprite-Unlit-Default material is unavailable.");
            int changed = 0;
            foreach (var name in new[] { "Player", "Enemy", "Projectile", "Pickup", "Effect" })
                changed += MigratePrefab("Assets/GameRes/Survivor/Prefabs/" + name + ".prefab", unlit);
            changed += MigratePrefab("Assets/SurvivorSample/Scenes/SurvivorArenaRoot.prefab", unlit);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Yato.Rendering] Source project configured for URP 2D Unlit; migrated {changed} renderers. " +
                $"Color space preserved: {PlayerSettings.colorSpace}; quality restored: {QualitySettings.GetQualityLevel()}. Rebuild source AssetBundles before Player verification.");
        }

        private static void ConfigureGlobalSettings()
        {
            var global = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>(GlobalPath)
                ?? GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>();
            if (global == null)
            {
                // The URP type is internal in 17.5. Use Unity's public creation utility so its
                // resource containers and volume profile are populated and initialized correctly.
                var type = typeof(UniversalRenderPipeline).Assembly.GetType(
                    "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings", true);
                global = RenderPipelineGlobalSettingsUtils.Create(type, GlobalPath);
            }
            MoveProjectAsset(global, GlobalPath);
            EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<UniversalRenderPipeline>(global);
            var defaults = GraphicsSettings.GetRenderPipelineSettings<URPDefaultVolumeProfileSettings>();
            if (defaults == null || defaults.volumeProfile == null)
                throw new InvalidOperationException("URP global settings did not initialize the default volume profile.");
            var profile = defaults.volumeProfile;
            string path = AssetDatabase.GetAssetPath(profile);
            if (path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) == null && !AssetDatabase.CopyAsset(path, ProfilePath))
                    throw new InvalidOperationException("Unable to copy the URP default profile into the source project.");
                defaults.volumeProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            }
            else MoveProjectAsset(profile, ProfilePath);
            EditorUtility.SetDirty(global);
        }

        private static void MoveProjectAsset(UnityEngine.Object asset, string destination)
        {
            string current = AssetDatabase.GetAssetPath(asset);
            if (current == destination) return;
            if (!current.StartsWith("Assets/", StringComparison.Ordinal) || AssetDatabase.LoadMainAssetAtPath(destination) != null)
                throw new InvalidOperationException($"Refusing to replace an existing rendering asset: {current} -> {destination}");
            string error = AssetDatabase.MoveAsset(current, destination);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
        }

        private static int MigratePrefab(string path, Material unlit)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int changed = 0;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(renderer is SpriteRenderer) && !(renderer is TilemapRenderer)) continue;
                    var materials = renderer.sharedMaterials;
                    bool replaced = false;
                    for (int index = 0; index < materials.Length; index++)
                    {
                        if (materials[index] != null &&
                            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(materials[index], out string guid, out long fileId) &&
                            guid == "0000000000000000f000000000000000" && fileId == 10754)
                        {
                            materials[index] = unlit;
                            replaced = true;
                        }
                    }
                    if (!replaced) continue;
                    renderer.sharedMaterials = materials;
                    changed++;
                }
                if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                return changed;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
