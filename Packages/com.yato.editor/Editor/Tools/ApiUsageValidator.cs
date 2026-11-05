using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Yato.Editor
{
    public enum ApiUsageSeverity
    {
        Warning,
        Error
    }

    /// <summary>A single forbidden-API rule. Patterns are matched per line in non-comment code.</summary>
    public sealed class ApiUsageRule
    {
        public readonly string Id;
        public readonly string Pattern;
        public readonly ApiUsageSeverity Severity;
        public readonly string Suggestion;

        public ApiUsageRule(string id, string pattern, ApiUsageSeverity severity, string suggestion)
        {
            Id = id;
            Pattern = pattern;
            Severity = severity;
            Suggestion = suggestion;
        }
    }

    public sealed class ApiUsageOptions
    {
        public bool ScanAssets = true;
        public bool ScanPackages;
        public bool ExcludeTests = true;
        public int MaxFileBytes = 2 * 1024 * 1024;

        /// <summary>额外扫描根（绝对或相对工程根）。用于验证规则或扫描生成目录。</summary>
        public readonly List<string> CustomRoots = new List<string>();

        public static ApiUsageOptions Default()
        {
            return new ApiUsageOptions();
        }

        /// <summary>Build gate: project assets only; Tests are legitimate fixtures.</summary>
        public static ApiUsageOptions ForBuild()
        {
            return new ApiUsageOptions { ScanAssets = true, ScanPackages = false, ExcludeTests = true };
        }
    }

    public sealed class ApiUsageHit
    {
        public string Path;
        public int Line;
        public string RuleId;
        public ApiUsageSeverity Severity;
        public string Text;

        public override string ToString()
        {
            return string.Format("{0}({1}) [{2}] {3}  {4}", Path, Line, RuleId, Severity, Text);
        }
    }

    public sealed class ApiUsageReport
    {
        public readonly List<ApiUsageHit> Hits = new List<ApiUsageHit>();
        public readonly List<string> Roots = new List<string>();
        public int ScannedFiles;

        public int ErrorCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Hits.Count; i++)
                    if (Hits[i].Severity == ApiUsageSeverity.Error) count++;
                return count;
            }
        }

        public bool Success
        {
            get { return ErrorCount == 0; }
        }

        public string ToJson()
        {
            var builder = new StringBuilder(1024);
            builder.Append("{\"success\":").Append(Success ? "true" : "false");
            builder.Append(",\"scannedFiles\":").Append(ScannedFiles);
            builder.Append(",\"roots\":[");
            for (int i = 0; i < Roots.Count; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append('"').Append(Escape(Roots[i])).Append('"');
            }

            builder.Append("],\"hits\":[");
            for (int i = 0; i < Hits.Count; i++)
            {
                ApiUsageHit hit = Hits[i];
                if (i > 0) builder.Append(',');
                builder.Append("{\"path\":\"").Append(Escape(hit.Path))
                    .Append("\",\"line\":").Append(hit.Line)
                    .Append(",\"rule\":\"").Append(Escape(hit.RuleId))
                    .Append("\",\"severity\":\"").Append(hit.Severity)
                    .Append("\",\"text\":\"").Append(Escape(hit.Text))
                    .Append("\"}");
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Forbidden-API scan with structured results, an optional build gate and a batch entry point.
    /// Rules cover resource loading, direct scene switching and global time scale writes; Editor
    /// tooling, generated code and test fixtures are excluded by default. A line can opt out with
    /// <c>// yato:allow &lt;rule-id&gt;</c> when the call is intentionally outside the framework.
    /// </summary>
    public static class ApiUsageValidator
    {
        public const string EnforceInBuildKey = "Yato.ApiUsage.EnforceInBuild";

        private static readonly ApiUsageRule[] Rules =
        {
            // Only UnityEngine.Resources itself. A property named Resources (for example
            // IResourceService Resources) must not be reported as a forbidden global load.
            new ApiUsageRule("resources-load", @"(?<![\w.])(?:UnityEngine\.)?Resources\s*\.\s*(Load|LoadAsync|LoadAll)\s*[<(]",
                ApiUsageSeverity.Error, "改用 IResourceService / ResourceScope 加载资源"),
            new ApiUsageRule("scene-load-direct", @"\bSceneManager\s*\.\s*LoadScene(Async)?\s*\(",
                ApiUsageSeverity.Error, "改用 ISceneService 加载场景"),
            new ApiUsageRule("timescale-write", @"\bTime\s*\.\s*timeScale\s*=(?!=)",
                ApiUsageSeverity.Error, "改用 PauseService 控制暂停"),
            new ApiUsageRule("assetdatabase-load", @"\bAssetDatabase\s*\.\s*(LoadAssetAtPath|LoadAllAssetsAtPath|LoadAssetAtPathAsync)\b",
                ApiUsageSeverity.Warning, "AssetDatabase 只能在 Editor 工具中使用，运行期资源请走 IResourceService")
        };

        private static readonly Regex[] Compiled = BuildPatterns();

        [MenuItem("Yato/Tools/Check Forbidden APIs", false, 402)]
        public static void Check()
        {
            ApiUsageReport report = Scan();
            WriteReport(report);

            if (report.Success)
            {
                Debug.Log(string.Format("[Yato.Verify] 未发现禁用 API（扫描 {0} 个文件），构建门禁：{1}",
                    report.ScannedFiles, EnforceInBuild ? "启用" : "关闭"));
                return;
            }

            int shown = 0;
            for (int i = 0; i < report.Hits.Count && shown < 50; i++)
            {
                if (report.Hits[i].Severity != ApiUsageSeverity.Error) continue;
                Debug.LogWarning("[Yato.Verify] " + report.Hits[i]);
                shown++;
            }

            Debug.LogWarningFormat("[Yato.Verify] 共 {0} 处错误、{1} 处警告，详见 {2}",
                report.ErrorCount, report.Hits.Count - report.ErrorCount, ReportPath);
        }

        /// <summary>Batch entry point: <c>-executeMethod Yato.Editor.ApiUsageValidator.ValidateForBatch</c>.</summary>
        public static void ValidateForBatch()
        {
            ApiUsageReport report = Scan(ApiUsageOptions.ForBuild());
            WriteReport(report);
            if (!report.Success)
            {
                throw new InvalidOperationException(string.Format(
                    "Forbidden API usage: {0} error(s), report: {1}", report.ErrorCount, ReportPath));
            }

            Debug.Log(string.Format("[Yato.Verify] Forbidden API scan passed ({0} files)", report.ScannedFiles));
        }

        public static ApiUsageReport Scan(ApiUsageOptions options = null)
        {
            options = options ?? ApiUsageOptions.Default();
            var report = new ApiUsageReport();
            string projectRoot = ProjectRoot;

            if (options.ScanAssets) ScanRoot(Path.Combine(projectRoot, "Assets"), projectRoot, options, report);
            if (options.ScanPackages) ScanRoot(Path.Combine(projectRoot, "Packages"), projectRoot, options, report);
            for (int i = 0; i < options.CustomRoots.Count; i++)
            {
                string root = options.CustomRoots[i];
                if (!Path.IsPathRooted(root)) root = Path.Combine(projectRoot, root);
                ScanRoot(root, projectRoot, options, report);
            }

            report.Hits.Sort(delegate(ApiUsageHit left, ApiUsageHit right)
            {
                int path = string.CompareOrdinal(left.Path, right.Path);
                return path != 0 ? path : left.Line.CompareTo(right.Line);
            });
            return report;
        }

        private static void ScanRoot(string root, string projectRoot, ApiUsageOptions options, ApiUsageReport report)
        {
            if (!Directory.Exists(root)) return;
            report.Roots.Add(ToProjectRelative(root, projectRoot));

            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                if (IsExcluded(path, options)) continue;
                if (new FileInfo(path).Length > options.MaxFileBytes) continue;

                report.ScannedFiles++;
                ScanFile(path, ToProjectRelative(path, projectRoot), report);
            }
        }

        private static void ScanFile(string absolutePath, string relativePath, ApiUsageReport report)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(absolutePath);
            }
            catch (IOException e)
            {
                Debug.LogWarning("[Yato.Verify] 无法读取 " + relativePath + ": " + e.Message);
                return;
            }

            bool inBlockComment = false;
            for (int line = 0; line < lines.Length; line++)
            {
                string original = lines[line];
                if (IsExempt(original, null)) continue;

                string code = StripComments(original, ref inBlockComment);
                if (code.Trim().Length == 0) continue;

                for (int rule = 0; rule < Rules.Length; rule++)
                {
                    if (!Compiled[rule].IsMatch(code)) continue;
                    if (IsExempt(original, Rules[rule].Id)) continue;
                    report.Hits.Add(new ApiUsageHit
                    {
                        Path = relativePath,
                        Line = line + 1,
                        RuleId = Rules[rule].Id,
                        Severity = Rules[rule].Severity,
                        Text = original.Trim()
                    });
                }
            }
        }

        /// <summary>
        /// Removes line and block comments while preserving string/char literals so URLs and
        /// quoted text are not mistaken for comments. Commented-out calls must not fail a build.
        /// </summary>
        private static string StripComments(string line, ref bool inBlockComment)
        {
            var builder = new StringBuilder(line.Length);
            int i = 0;
            bool inString = false;
            char quote = '\0';
            while (i < line.Length)
            {
                if (inBlockComment)
                {
                    int end = line.IndexOf("*/", i, StringComparison.Ordinal);
                    if (end < 0) return builder.ToString();
                    i = end + 2;
                    inBlockComment = false;
                    continue;
                }

                char c = line[i];
                if (inString)
                {
                    builder.Append(c);
                    if (c == '\\' && i + 1 < line.Length)
                    {
                        builder.Append(line[i + 1]);
                        i += 2;
                        continue;
                    }

                    if (c == quote) inString = false;
                    i++;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                    builder.Append(c);
                    i++;
                    continue;
                }

                if (c == '/' && i + 1 < line.Length)
                {
                    if (line[i + 1] == '/') return builder.ToString();
                    if (line[i + 1] == '*')
                    {
                        inBlockComment = true;
                        i += 2;
                        continue;
                    }
                }

                builder.Append(c);
                i++;
            }

            return builder.ToString();
        }

        /// <summary>
        /// A line containing <c>yato:allow</c> is skipped entirely; passing a rule id keeps the
        /// check precise, while a null rule id answers "does this line opt out at all?".
        /// </summary>
        private static bool IsExempt(string line, string ruleId)
        {
            const string marker = "yato:allow";
            int index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            if (string.IsNullOrEmpty(ruleId)) return true;
            string remainder = line.Substring(index + marker.Length).Trim();
            return remainder.Length == 0 || remainder.StartsWith(ruleId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExcluded(string path, ApiUsageOptions options)
        {
            if (path.Contains("/Editor/") || path.Contains("/Samples~/") || path.Contains("/YatoGenerated/"))
                return true;
            if (options.ExcludeTests && (path.Contains("/Tests/") || path.Contains("/Test/")))
                return true;
            return false;
        }

        private static bool EnforceInBuild
        {
            get { return EditorPrefs.GetBool(EnforceInBuildKey, true); }
        }

        private static string ReportPath
        {
            get { return Path.Combine(Path.Combine(ProjectRoot, "Logs"), "api-usage-report.json"); }
        }

        private static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/'); }
        }

        private static string ToProjectRelative(string path, string projectRoot)
        {
            string normalized = path.Replace('\\', '/');
            string root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(root.Length)
                : normalized;
        }

        private static void WriteReport(ApiUsageReport report)
        {
            try
            {
                string path = ReportPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, report.ToJson(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Yato.Verify] 无法写入扫描报告：" + e.Message);
            }
        }

        private static Regex[] BuildPatterns()
        {
            var result = new Regex[Rules.Length];
            for (int i = 0; i < Rules.Length; i++)
                result[i] = new Regex(Rules[i].Pattern, RegexOptions.Compiled);
            return result;
        }
    }

    /// <summary>Optional build gate. Disable with EditorPrefs "Yato.ApiUsage.EnforceInBuild" = false.</summary>
    public sealed class ApiUsageBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder
        {
            get { return 0; }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!EditorPrefs.GetBool(ApiUsageValidator.EnforceInBuildKey, true)) return;

            ApiUsageReport result = ApiUsageValidator.Scan(ApiUsageOptions.ForBuild());
            if (result.Success) return;

            throw new BuildFailedException(string.Format(
                "Yato forbidden API check failed: {0} error(s). Run Yato/Tools/Check Forbidden APIs for details.",
                result.ErrorCount));
        }
    }
}
