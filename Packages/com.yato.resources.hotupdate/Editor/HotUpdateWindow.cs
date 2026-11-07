using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Yato.Resources;

namespace Yato.Editor
{
    /// <summary>
    /// 热更设置与「模拟远程」开关（T-P5-05）。
    /// 本地验证：把 Build/AssetBundles 当作远端根目录，用任意静态服务器暴露即可。
    /// </summary>
    public sealed class HotUpdateWindow : EditorWindow
    {
        private const string UrlKey = "Yato.HotUpdate.RemoteRootUrl";

        private string m_url;

        [MenuItem("Yato/HotUpdate/Settings", false, 500)]
        public static void Open()
        {
            var window = GetWindow<HotUpdateWindow>("Yato HotUpdate");
            window.minSize = new Vector2(480f, 200f);
            window.m_url = EditorPrefs.GetString(UrlKey, "http://127.0.0.1:8080");
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("远端根地址（不含平台目录）");
            m_url = EditorGUILayout.TextField(m_url);

            EditorGUILayout.Space();
            if (GUILayout.Button("保存并应用到当前会话"))
            {
                EditorPrefs.SetString(UrlKey, m_url);
                RuntimePaths.RemoteRootUrl = m_url;
                Debug.Log("[Yato.HotUpdate] RemoteRootUrl = " + m_url);
            }

            if (GUILayout.Button("用本地 Build 目录模拟远程"))
            {
                string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Build", "AssetBundles");
                if (!Directory.Exists(dir))
                {
                    Debug.LogError("[Yato.HotUpdate] 目录不存在，请先打包：" + dir);
                }
                else
                {
                    m_url = new Uri(dir).AbsoluteUri.TrimEnd('/');
                    EditorPrefs.SetString(UrlKey, m_url);
                    RuntimePaths.RemoteRootUrl = m_url;
                    Debug.Log("[Yato.HotUpdate] 模拟远程 = " + m_url);
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("重新打包并生成 manifest/version"))
            {
                if (!EditorApplication.ExecuteMenuItem("Yato/Build/Bundles (Active Platform)"))
                    Debug.LogError("[Yato.HotUpdate] 找不到 Bundle 打包菜单，请检查 com.yato.resources.bundle 是否已安装并成功编译。");
            }

            EditorGUILayout.HelpBox(
                "本地模拟步骤：\n" +
                "1) 打包（生成 Build/AssetBundles/{Platform}/manifest.json 与 version.json）\n" +
                "2) 在 Build/AssetBundles 下启动静态服务器，如 python -m http.server 8080\n" +
                "3) 本地测试需显式创建 RemoteDownloader { AllowFileUrls = true, AllowInsecureLocalhost = true }\n" +
                "4) 生产仅使用 HTTPS；旧 Provider 固定旧快照，新建 Provider 才使用已提交版本",
                MessageType.Info);
        }
    }
}
