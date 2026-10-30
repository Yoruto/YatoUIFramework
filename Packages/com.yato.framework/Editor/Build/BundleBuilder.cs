using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Yato.Resources;

namespace Yato.Editor
{
    /// <summary>打包结果。</summary>
    public sealed class BundleBuildResult
    {
        public BuildTarget Target;
        public string OutputDir;
        public string ManifestPath;
        public int BundleCount;
        public int AssetCount;
        public long TotalBytes;

        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public bool Success
        {
            get { return Errors.Count == 0; }
        }
    }

    /// <summary>
    /// 参数化打包入口（取代 Bang- 的 AssertBundleCreator：4 个平台菜单各自硬编码，RES-18）。
    /// 规则：目录即 bundle（<see cref="BundleNaming"/>），LZ4 压缩，不追加 hash（由 manifest 管理）。
    /// </summary>
    public static class BundleBuilder
    {
        public const string MenuRoot = "Yato/Build/";

        [MenuItem(MenuRoot + "Bundles (Active Platform)", false, 100)]
        public static void BuildActivePlatform()
        {
            BundleBuildResult result = Build(EditorUserBuildSettings.activeBuildTarget);
            ReportResult(result);
        }

        [MenuItem(MenuRoot + "Bundles (Android)", false, 101)]
        public static void BuildAndroid()
        {
            ReportResult(Build(BuildTarget.Android));
        }

        [MenuItem(MenuRoot + "Bundles (Windows)", false, 102)]
        public static void BuildWindows()
        {
            ReportResult(Build(BuildTarget.StandaloneWindows64));
        }

        [MenuItem(MenuRoot + "Bundles (iOS)", false, 103)]
        public static void BuildiOS()
        {
            ReportResult(Build(BuildTarget.iOS));
        }

        [MenuItem(MenuRoot + "Bundles (WebGL)", false, 104)]
        public static void BuildWebGL()
        {
            ReportResult(Build(BuildTarget.WebGL));
        }

        /// <summary>打包指定平台。</summary>
        public static BundleBuildResult Build(
            BuildTarget target,
            string assetRoot = BundleNaming.DefaultAssetRoot,
            string version = "1.0.0",
            bool copyToStreamingAssets = true)
        {
            var result = new BundleBuildResult { Target = target };

            try
            {
                string warning = PlatformResolver.ValidateTarget(target);
                if (!string.IsNullOrEmpty(warning))
                {
                    result.Warnings.Add(warning);
                }

                List<string> assets = BundleNaming.CollectBuildableAssets(assetRoot);
                if (assets.Count == 0)
                {
                    throw new InvalidOperationException(
                        string.Format("资源根目录下没有可打包资源：{0}（检查 BundleNaming.BuildableExtensions）", assetRoot));
                }

                var groups = new Dictionary<string, ManifestBuilder.BundleAssetGroup>(StringComparer.Ordinal);
                for (int i = 0; i < assets.Count; i++)
                {
                    string assetPath = assets[i];
                    string bundleName = BundleNaming.GetBundleName(assetPath, assetRoot);
                    string assetKey = BundleNaming.GetAssetKey(assetPath, assetRoot);

                    if (string.IsNullOrEmpty(bundleName) || string.IsNullOrEmpty(assetKey))
                    {
                        result.Warnings.Add("跳过资源（不在资源根下）：" + assetPath);
                        continue;
                    }

                    ManifestBuilder.BundleAssetGroup group;
                    if (!groups.TryGetValue(bundleName, out group))
                    {
                        group = new ManifestBuilder.BundleAssetGroup { BundleName = bundleName };
                        groups[bundleName] = group;
                    }

                    group.AssetPaths.Add(assetPath);
                    group.AssetKeys.Add(assetKey);
                }

                if (groups.Count == 0)
                {
                    throw new InvalidOperationException("没有生成任何 bundle，请检查资源目录结构");
                }

                var builds = new AssetBundleBuild[groups.Count];
                int index = 0;
                foreach (ManifestBuilder.BundleAssetGroup group in groups.Values)
                {
                    builds[index++] = new AssetBundleBuild
                    {
                        assetBundleName = group.BundleName,
                        assetNames = group.AssetPaths.ToArray()
                    };
                }

                string outputDir = PlatformResolver.GetBuildOutputDir(target);
                if (Directory.Exists(outputDir))
                {
                    Directory.Delete(outputDir, true);
                }

                Directory.CreateDirectory(outputDir);

                AssetBundleManifest unityManifest = BuildPipeline.BuildAssetBundles(
                    outputDir,
                    builds,
                    BuildAssetBundleOptions.ChunkBasedCompression,
                    target);

                if (unityManifest == null)
                {
                    throw new InvalidOperationException(
                        "BuildAssetBundles 返回 null：请确认目标平台模块已安装，且 Console 无编译错误");
                }

                var groupList = new List<ManifestBuilder.BundleAssetGroup>(groups.Values);
                string manifestPath = ManifestBuilder.Write(
                    unityManifest,
                    groupList,
                    outputDir,
                    PlatformResolver.GetPlatformName(target),
                    version);

                result.OutputDir = outputDir;
                result.ManifestPath = manifestPath;
                result.BundleCount = groupList.Count;
                result.AssetCount = assets.Count;

                Validate(outputDir, manifestPath, assetRoot, result);

                if (copyToStreamingAssets)
                {
                    CopyToStreamingAssets(outputDir, target);
                }
            }
            catch (Exception e)
            {
                result.Errors.Add(e.Message);
                Debug.LogException(e);
            }
            finally
            {
                AssetDatabase.Refresh();
            }

            return result;
        }

        /// <summary>校验产物与 manifest 的一致性，并把冗余依赖写入警告。</summary>
        private static void Validate(string outputDir, string manifestPath, string assetRoot, BundleBuildResult result)
        {
            ResourceManifest manifest;
            try
            {
                manifest = ResourceManifest.LoadFromFile(manifestPath);
            }
            catch (Exception e)
            {
                result.Errors.Add("manifest 自检失败：" + e.Message);
                return;
            }

            foreach (KeyValuePair<string, ResourceManifest.BundleInfo> pair in manifest.Bundles)
            {
                string file = Path.Combine(outputDir, pair.Key);
                if (!File.Exists(file))
                {
                    result.Errors.Add("bundle 文件缺失：" + pair.Key);
                    continue;
                }

                result.TotalBytes += new FileInfo(file).Length;
            }

            foreach (KeyValuePair<string, string> pair in manifest.Assets)
            {
                if (!manifest.Bundles.ContainsKey(pair.Value))
                {
                    result.Errors.Add(string.Format("资源 {0} 指向不存在的 bundle {1}", pair.Key, pair.Value));
                }
            }

            // UI 预制体必须全部可寻址（文档 6.3 打包后自动校验）
            string uiRoot = Path.Combine(assetRoot, "UI");
            if (Directory.Exists(uiRoot))
            {
                string[] prefabs = Directory.GetFiles(uiRoot, "*.prefab", SearchOption.AllDirectories);
                for (int i = 0; i < prefabs.Length; i++)
                {
                    string path = prefabs[i].Replace('\\', '/');
                    string key = BundleNaming.GetAssetKey(path, assetRoot);
                    if (!string.IsNullOrEmpty(key) && !manifest.ContainsAsset(key))
                    {
                        result.Warnings.Add("UI 预制体未进入 manifest：" + key);
                    }
                }
            }

            BuildReportAnalyzer.Analyze(manifest, result);
        }

        private static void CopyToStreamingAssets(string outputDir, BuildTarget target)
        {
            string destination = PlatformResolver.GetStreamingAssetsDir(target);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            Directory.CreateDirectory(destination);

            string[] files = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string relative = files[i].Substring(outputDir.Length).TrimStart('\\', '/');
                string targetPath = Path.Combine(destination, relative);
                string directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.Copy(files[i], targetPath, true);
            }

            Debug.Log(string.Format("[Yato.Build] 产物已同步到 {0}", destination));
        }

        private static void ReportResult(BundleBuildResult result)
        {
            if (result == null)
            {
                return;
            }

            for (int i = 0; i < result.Warnings.Count; i++)
            {
                Debug.LogWarning("[Yato.Build] " + result.Warnings[i]);
            }

            if (!result.Success)
            {
                for (int i = 0; i < result.Errors.Count; i++)
                {
                    Debug.LogError("[Yato.Build] " + result.Errors[i]);
                }

                EditorUtility.DisplayDialog(
                    "Yato Build",
                    string.Format("打包失败，共 {0} 个错误，详见 Console", result.Errors.Count),
                    "确定");
                return;
            }

            Debug.Log(string.Format(
                "[Yato.Build] 完成：{0} 个 bundle / {1} 个资源 / {2:F1} MB → {3}",
                result.BundleCount,
                result.AssetCount,
                result.TotalBytes / 1024f / 1024f,
                result.ManifestPath));
        }
    }
}
