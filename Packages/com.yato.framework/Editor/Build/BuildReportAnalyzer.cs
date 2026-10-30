using System.Collections.Generic;
using System.Text;
using Yato.Resources;

namespace Yato.Editor
{
    /// <summary>
    /// 冗余依赖分析（文档 6.4 第 2 条）。
    /// 基于打包后的 AssetBundleManifest 依赖关系统计「被多少个 bundle 依赖」，
    /// 超过阈值时提示拆公共包，避免资源重复打进多个 bundle。
    /// </summary>
    public static class BuildReportAnalyzer
    {
        /// <summary>被多少个 bundle 依赖时给出提示。</summary>
        public const int SharedDependencyThreshold = 3;

        public static void Analyze(ResourceManifest manifest, BundleBuildResult result)
        {
            if (manifest == null || result == null)
            {
                return;
            }

            var usage = new Dictionary<string, int>(System.StringComparer.Ordinal);
            foreach (KeyValuePair<string, ResourceManifest.BundleInfo> pair in manifest.Bundles)
            {
                List<string> dependencies = pair.Value.Dependencies;
                for (int i = 0; i < dependencies.Count; i++)
                {
                    string dependency = dependencies[i];
                    int count;
                    usage.TryGetValue(dependency, out count);
                    usage[dependency] = count + 1;
                }
            }

            var shared = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in usage)
            {
                if (pair.Value < SharedDependencyThreshold)
                {
                    continue;
                }

                shared.Append(pair.Key).Append("(").Append(pair.Value).Append(") ");
            }

            if (shared.Length > 0)
            {
                result.Warnings.Add(
                    "以下 bundle 被多个包依赖，建议拆为公共包或合并来源：" + shared.ToString().TrimEnd());
            }
        }
    }
}
