using System;
using UnityEditor;
using UnityEngine;

namespace Yato.Editor
{
    /// <summary>
    /// CI 打包入口。与编辑器菜单共用同一实现，保证本地与流水线一致（T-P4-07）。
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; \
    ///   -executeMethod Yato.Editor.BuildPipelineEntry.BuildBundles -buildTarget Android
    /// </code>
    /// </summary>
    public static class BuildPipelineEntry
    {
        /// <summary>按当前激活平台打包资源。</summary>
        public static void BuildBundles()
        {
            Run(delegate { return BundleBuilder.Build(EditorUserBuildSettings.activeBuildTarget); });
        }

        public static void BuildBundlesAndroid()
        {
            Run(delegate { return BundleBuilder.Build(BuildTarget.Android); });
        }

        public static void BuildBundlesWindows()
        {
            Run(delegate { return BundleBuilder.Build(BuildTarget.StandaloneWindows64); });
        }

        public static void BuildBundlesiOS()
        {
            Run(delegate { return BundleBuilder.Build(BuildTarget.iOS); });
        }

        public static void BuildBundlesWebGL()
        {
            Run(delegate { return BundleBuilder.Build(BuildTarget.WebGL); });
        }

        public static void RequireSuccess(BundleBuildResult result)
        {
            if (result == null || !result.Success)
                throw new InvalidOperationException(result == null ? "Bundle build returned no result" : string.Join("\n", result.Errors));
        }

        private static void Run(Func<BundleBuildResult> action)
        {
            try
            {
                RequireSuccess(action());
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }
    }
}
