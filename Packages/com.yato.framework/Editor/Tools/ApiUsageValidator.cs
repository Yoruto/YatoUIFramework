using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Yato.Editor
{
    /// <summary>
    /// 禁用 API 静态检查（文档 10 风险应对）：业务代码不得直接调用 Resources.Load / AssetDatabase 加载运行期资源。
    /// 菜单：Yato/Tools/Check Forbidden APIs。
    /// </summary>
    public static class ApiUsageValidator
    {
        private static readonly Regex ForbiddenPattern = new Regex(
            @"\bResources\.(Load|LoadAsync|LoadAll)\s*[<(]",
            RegexOptions.Compiled);

        [MenuItem("Yato/Tools/Check Forbidden APIs", false, 402)]
        public static void Check()
        {
            var hits = new List<string>();
            string[] files = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories);

            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                if (path.Contains("/Editor/") || path.Contains("/YatoGenerated/"))
                {
                    continue;
                }

                string[] lines = File.ReadAllLines(path);
                for (int line = 0; line < lines.Length; line++)
                {
                    if (ForbiddenPattern.IsMatch(lines[line]))
                    {
                        hits.Add(string.Format("{0}:{1}  {2}", path, line + 1, lines[line].Trim()));
                    }
                }
            }

            if (hits.Count == 0)
            {
                Debug.Log("[Yato.Verify] 未发现直接调用 Resources.Load 的业务代码");
                return;
            }

            for (int i = 0; i < hits.Count; i++)
            {
                Debug.LogWarning("[Yato.Verify] 禁止直接调用：" + hits[i]);
            }

            Debug.LogWarningFormat("[Yato.Verify] 共 {0} 处，请改用 IResourceService", hits.Count);
        }
    }
}
