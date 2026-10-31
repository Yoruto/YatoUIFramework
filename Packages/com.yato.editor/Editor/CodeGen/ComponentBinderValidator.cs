using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using Yato.UI;

namespace Yato.Editor
{
    /// <summary>
    /// 预制体静态校验（文档 7.4 / T-P4-04）：
    /// 1) ComponentBinder 绑定（空名称 / 重名 / 空引用）；
    /// 2) 绑定名与目标节点名不一致（防重排后绑错，DEF-13）；
    /// 3) TMP 字体缺失（font / fontSharedMaterial 为空，运行时会显示空白）；
    /// 4) 预制体命名（含空格或非 ASCII 字符）。
    /// 菜单：Yato/UI/Validate Component Binders。
    /// </summary>
    public static class ComponentBinderValidator
    {
        private const string SearchRoot = "Assets/GameRes";

        [MenuItem("Yato/UI/Validate Component Binders", false, 200)]
        public static void ValidateAll()
        {
            if (!AssetDatabase.IsValidFolder(SearchRoot))
            {
                Debug.LogWarning("[Yato.Verify] 未找到资源根目录：" + SearchRoot);
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SearchRoot });
            int prefabCount = 0;
            int binderCount = 0;
            int errorCount = 0;
            int warningCount = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                prefabCount++;
                errorCount += ValidatePrefab(prefab, path, out int binders, ref warningCount);
                binderCount += binders;
            }

            Debug.Log(string.Format(
                "[Yato.Verify] 校验完成：预制体 {0} 个，ComponentBinder {1} 个，错误 {2} 个，警告 {3} 个",
                prefabCount, binderCount, errorCount, warningCount));

            if (errorCount > 0)
            {
                EditorUtility.DisplayDialog(
                    "Yato Verify",
                    string.Format("发现 {0} 个错误、{1} 个警告，详见 Console", errorCount, warningCount),
                    "确定");
            }
        }

        private static int ValidatePrefab(GameObject prefab, string path, out int binderCount, ref int warningCount)
        {
            int errors = 0;
            var binders = prefab.GetComponentsInChildren<ComponentBinder>(true);
            binderCount = binders.Length;

            for (int b = 0; b < binders.Length; b++)
            {
                ComponentBinder binder = binders[b];

                if (!binder.Validate(out string error))
                {
                    errors++;
                    Debug.LogError("[Yato.Verify] " + error + " ← " + path, prefab);
                    continue;
                }

                if (binder.Bindings.Count == 0)
                {
                    warningCount++;
                    Debug.LogWarningFormat("[Yato.Verify] {0} 上的 ComponentBinder 没有任何绑定", path);
                    continue;
                }

                // 绑定名应与目标节点同名，重排/改名后能第一时间发现
                for (int i = 0; i < binder.Bindings.Count; i++)
                {
                    ComponentBinder.BindEntry entry = binder.Bindings[i];
                    if (entry == null || entry.Target == null)
                    {
                        continue;
                    }

                    if (!string.Equals(entry.Name, entry.Target.gameObject.name))
                    {
                        warningCount++;
                        Debug.LogWarningFormat(
                            "[Yato.Verify] {0} 绑定名与节点名不一致：{1} ≠ {2}",
                            path, entry.Name, entry.Target.gameObject.name);
                    }
                }
            }

            errors += ValidateTmpFonts(prefab, path, ref warningCount);
            errors += ValidateNaming(path, ref warningCount);
            return errors;
        }

        /// <summary>TMP 字体缺失会导致文本完全不显示，按错误处理。</summary>
        private static int ValidateTmpFonts(GameObject prefab, string path, ref int warningCount)
        {
            int errors = 0;
            var texts = prefab.GetComponentsInChildren<TMP_Text>(true);

            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text.font == null)
                {
                    errors++;
                    Debug.LogErrorFormat(
                        "[Yato.Verify] {0} 的节点 {1} 未指定 TMP 字体（font 为空），运行时会显示空白",
                        path, text.gameObject.name);
                    continue;
                }

                if (text.fontSharedMaterial == null)
                {
                    warningCount++;
                    Debug.LogWarningFormat(
                        "[Yato.Verify] {0} 的节点 {1} 缺少字体材质（fontSharedMaterial 为空）",
                        path, text.gameObject.name);
                }
            }

            return errors;
        }

        /// <summary>预制体名即页面 id，必须是可生成 C# 标识符的 ASCII 名称。</summary>
        private static int ValidateNaming(string path, ref int warningCount)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            for (int i = 0; i < fileName.Length; i++)
            {
                char c = fileName[i];
                if (c > 127 || c == ' ')
                {
                    warningCount++;
                    Debug.LogWarningFormat(
                        "[Yato.Verify] {0} 预制体名包含非 ASCII 或空格字符，生成的页面 id 可能不可用", path);
                    return 0;
                }
            }

            if (char.IsLower(fileName[0]))
            {
                warningCount++;
                Debug.LogWarningFormat("[Yato.Verify] {0} 预制体名建议用 PascalCase（如 BagForm）", path);
            }

            return 0;
        }

        /// <summary>供生成器复用：收集某预制体上所有绑定名与目标。</summary>
        public static Dictionary<string, Component> CollectBindings(GameObject prefab)
        {
            var result = new Dictionary<string, Component>();
            if (prefab == null)
            {
                return result;
            }

            ComponentBinder binder = prefab.GetComponentInChildren<ComponentBinder>(true);
            if (binder == null)
            {
                return result;
            }

            for (int i = 0; i < binder.Bindings.Count; i++)
            {
                ComponentBinder.BindEntry entry = binder.Bindings[i];
                if (entry != null && !string.IsNullOrEmpty(entry.Name) && !result.ContainsKey(entry.Name))
                {
                    result[entry.Name] = entry.Target;
                }
            }

            return result;
        }
    }
}
