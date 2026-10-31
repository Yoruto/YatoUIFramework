using System.IO;
using UnityEditor;
using UnityEngine;

namespace Yato.Editor
{
    /// <summary>
    /// 代码生成设置。生成物默认写到工程侧 <c>Assets/YatoGenerated</c>：
    /// 页面表需要 typeof 业务 Mediator，不能放在包内程序集。
    /// </summary>
    public static class CodeGenSettings
    {
        private const string OutputKey = "Yato.CodeGen.OutputFolder";
        private const string AssetRootKey = "Yato.CodeGen.AssetRoot";

        public const string DefaultOutputFolder = "Assets/YatoGenerated";
        public const string DefaultAssetRoot = "Assets/GameRes";

        /// <summary>生成目录（相对工程根）。</summary>
        public static string OutputFolder
        {
            get { return EditorPrefs.GetString(OutputKey, DefaultOutputFolder); }
            set { EditorPrefs.SetString(OutputKey, value); }
        }

        /// <summary>资源根目录（prefab 扫描起点）。</summary>
        public static string AssetRoot
        {
            get { return EditorPrefs.GetString(AssetRootKey, DefaultAssetRoot); }
            set { EditorPrefs.SetString(AssetRootKey, value); }
        }

        /// <summary>确保输出目录存在，返回目录路径。</summary>
        public static string EnsureOutputFolder()
        {
            string folder = OutputFolder;
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            return folder;
        }

        [MenuItem("Yato/CodeGen/Settings", false, 320)]
        public static void OpenSettings()
        {
            string folder = EditorUtility.OpenFolderPanel("选择代码生成目录（Assets 下）", OutputFolder, string.Empty);
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
            folder = folder.Replace('\\', '/');
            if (!folder.StartsWith(projectRoot))
            {
                Debug.LogError("[Yato.CodeGen] 生成目录必须位于当前工程 Assets 下");
                return;
            }

            string relative = folder.Substring(projectRoot.Length).TrimStart('/');
            if (!relative.StartsWith("Assets"))
            {
                Debug.LogError("[Yato.CodeGen] 生成目录必须位于 Assets 下");
                return;
            }

            OutputFolder = relative;
            Debug.Log("[Yato.CodeGen] 生成目录已设置为 " + relative);
        }
    }
}
