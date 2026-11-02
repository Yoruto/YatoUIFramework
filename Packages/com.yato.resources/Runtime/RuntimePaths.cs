using System;
using System.IO;
using UnityEngine;

namespace Yato.Resources
{
    /// <summary>
    /// 平台路径与远程地址的统一出口（RES-07/RES-16 的修复）。
    /// 所有 bundle 目录都按 <c>{root}/AssetBundles/{Platform}/</c> 组织，禁止手写平台字符串。
    /// </summary>
    public static class RuntimePaths
    {
        /// <summary>bundle 根目录名。</summary>
        public const string BundleFolderName = "AssetBundles";

        /// <summary>manifest 文件名。</summary>
        public const string ManifestFileName = "manifest.json";

        /// <summary>版本文件名（热更比对用）。</summary>
        public const string VersionFileName = "version.json";

        /// <summary>远程根地址；为空表示纯离线。</summary>
        public static string RemoteRootUrl { get; set; }

        /// <summary>是否优先使用 persistentDataPath 中的热更产物。</summary>
        public static bool PreferPersistent { get; set; }

        static RuntimePaths()
        {
            PreferPersistent = true;
        }

        /// <summary>当前运行平台名（与构建产物的子目录一致）。</summary>
        public static string PlatformName
        {
            get
            {
#if UNITY_ANDROID
                return "Android";
#elif UNITY_IOS
                return "iOS";
#elif UNITY_WEBGL
                return "WebGL";
#elif UNITY_STANDALONE_OSX
                return "OSX";
#elif UNITY_STANDALONE_LINUX
                return "Linux";
#elif UNITY_STANDALONE_WIN
                return "Windows";
#else
                return "Unknown";
#endif
            }
        }

        /// <summary>随包内置的只读目录。</summary>
        public static string StreamingAssetsBundleDir
        {
            get { return Path.Combine(Application.streamingAssetsPath, BundleFolderName, PlatformName); }
        }

        /// <summary>可写的热更目录。</summary>
        public static string PersistentBundleDir
        {
            get { return Path.Combine(Application.persistentDataPath, BundleFolderName, PlatformName); }
        }

        /// <summary>Android / WebGL 下 StreamingAssets 不是普通文件路径，必须走 UnityWebRequest。</summary>
        public static bool RequiresWebRequest
        {
            get
            {
#if UNITY_ANDROID || UNITY_WEBGL
                return true;
#else
                return false;
#endif
            }
        }

        public static string GetManifestPath(bool persistent)
        {
            string dir = persistent ? new HotUpdateStorage(PersistentBundleDir).ActiveDirectory : StreamingAssetsBundleDir;
            return Path.Combine(dir, ManifestFileName);
        }

        /// <summary>
        /// 解析某个 bundle 的实际路径：热更目录存在则用热更目录，否则回退内置目录。
        /// WebGL/Android 下返回 URL，其余返回文件系统路径。
        /// </summary>
        public static string ResolveBundlePath(string bundleName)
        {
            BundleRelativePath.ValidateBundle(bundleName);
            if (PreferPersistent)
            {
                string active = new HotUpdateStorage(PersistentBundleDir).ActiveDirectory;
                if (File.Exists(BundleRelativePath.Resolve(active, ManifestFileName)))
                    return ResolveBundlePath(bundleName, active);
            }
            return ResolveBundlePath(bundleName, StreamingAssetsBundleDir);
        }

        /// <summary>Resolve against a provider's fixed snapshot. Missing files never fall through to another version.</summary>
        public static string ResolveBundlePath(string bundleName, string snapshotDirectory)
        {
            BundleRelativePath.ValidateBundle(bundleName);
            HotUpdateMaintenance.MarkUsed(snapshotDirectory);
            if (snapshotDirectory.Contains("://") || snapshotDirectory.StartsWith("jar:", StringComparison.Ordinal))
                return snapshotDirectory.TrimEnd('/') + "/" + bundleName;
            return ToLoadPath(BundleRelativePath.Resolve(snapshotDirectory, bundleName));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PreferPersistent = true;
            RemoteRootUrl = null;
        }

        /// <summary>把物理路径转换成 UnityWebRequest 可用的 URL（按平台）。</summary>
        public static string ToLoadPath(string physicalPath)
        {
            if (string.IsNullOrEmpty(physicalPath))
            {
                return string.Empty;
            }

            if (physicalPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                physicalPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                physicalPath.StartsWith("jar:", StringComparison.OrdinalIgnoreCase) ||
                physicalPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                return physicalPath;
            }

            if (RequiresWebRequest)
            {
                return "file://" + physicalPath.Replace('\\', '/');
            }

            return physicalPath.Replace('\\', '/');
        }

        /// <summary>拼出远程地址，例如 <c>{RemoteRootUrl}/{Platform}/{file}</c>。</summary>
        public static string ToRemoteUrl(string fileName)
        {
            if (string.IsNullOrEmpty(RemoteRootUrl) || string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            string root = RemoteRootUrl.TrimEnd('/');
            return new RemoteDownloader().FileUrl(root, PlatformName + "/" + fileName);
        }

        /// <summary>确保可写目录存在。</summary>
        public static void EnsurePersistentDir()
        {
            try
            {
                if (!Directory.Exists(PersistentBundleDir))
                {
                    Directory.CreateDirectory(PersistentBundleDir);
                }
            }
            catch (Exception e)
            {
                Yato.Core.Log.Error("RuntimePaths", "创建持久化目录失败：" + PersistentBundleDir, e);
            }
        }
    }
}
