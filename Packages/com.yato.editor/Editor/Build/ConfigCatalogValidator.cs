using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Yato.Config;

namespace Yato.Editor
{
    public sealed class ConfigCatalogValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPreprocessBuild(BuildReport report) { ValidateAll(); }
        [MenuItem("Yato/Config/Validate Catalogs")]
        public static void ValidateAll()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ConfigCatalog"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var catalog = AssetDatabase.LoadAssetAtPath<ConfigCatalog>(path);
                if (catalog == null) continue;
                var errors = catalog.Validate();
                if (errors.Count > 0) throw new BuildFailedException(path + ": " + string.Join("\n", errors));
            }
        }
    }
}
