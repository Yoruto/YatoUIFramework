using System;
using UnityEditor;
using UnityEngine;

namespace Yato.Editor
{
    /// <summary>
    /// 构建平台 → 产物目录名的唯一映射（RES-19 的修复：不再出现 StandaloneLinux64 → "Android"）。
    /// 与运行时 <c>Yato.Resources.RuntimePaths.PlatformName</c> 保持一致。
    /// </summary>
    public static class PlatformResolver
    {
        /// <summary>当前激活平台对应的产物目录名。</summary>
        public static string ActivePlatformName
        {
            get { return GetPlatformName(EditorUserBuildSettings.activeBuildTarget); }
        }

        /// <summary>平台目录名，例如 Windows / Android / iOS / WebGL / OSX / Linux。</summary>
        public static string GetPlatformName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "Windows";
                case BuildTarget.Android:
                    return "Android";
                case BuildTarget.iOS:
                    return "iOS";
                case BuildTarget.WebGL:
                    return "WebGL";
                case BuildTarget.StandaloneOSX:
                    return "OSX";
                case BuildTarget.StandaloneLinux64:
                    return "Linux";
                default:
                    throw new ArgumentOutOfRangeException(
                        "target", target, "未支持的打包平台，请先在 PlatformResolver 中登记");
            }
        }

        /// <summary>中间产物目录：&lt;工程根&gt;/Build/AssetBundles/{Platform}。</summary>
        public static string GetBuildOutputDir(BuildTarget target)
        {
            return string.Format("{0}/Build/AssetBundles/{1}", GetProjectRoot(), GetPlatformName(target));
        }

        /// <summary>随包目录：Assets/StreamingAssets/AssetBundles/{Platform}。</summary>
        public static string GetStreamingAssetsDir(BuildTarget target)
        {
            return string.Format(
                "Assets/StreamingAssets/AssetBundles/{0}",
                GetPlatformName(target));
        }

        /// <summary>工程根目录（绝对路径）。</summary>
        public static string GetProjectRoot()
        {
            return System.IO.Path.GetDirectoryName(Application.dataPath);
        }

        /// <summary>校验平台与当前编辑器环境是否匹配，返回警告文本（null 表示无警告）。</summary>
        public static string ValidateTarget(BuildTarget target)
        {
            BuildTarget active = EditorUserBuildSettings.activeBuildTarget;
            if (active == target)
            {
                return null;
            }

            return string.Format(
                "当前激活平台为 {0}，即将构建 {1}；若模块未安装（Android/iOS/WebGL）请先在 Build Settings 切换或安装模块",
                GetPlatformName(active),
                GetPlatformName(target));
        }
    }
}
