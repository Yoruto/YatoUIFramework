using System;
using System.IO;
using NUnit.Framework;
using Yato.Editor;

namespace Yato.Tests.EditMode
{
    /// <summary>
    /// The forbidden-API scan must be precise enough to be a build gate: it reports real calls,
    /// ignores comments and exempt lines, and can scan an isolated root for verification.
    /// </summary>
    public sealed class ApiUsageValidatorTests
    {
        private string m_root;

        [SetUp]
        public void Setup()
        {
            m_root = Path.Combine(Path.GetTempPath(), "yato-api-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
        }

        [TearDown]
        public void Teardown()
        {
            if (Directory.Exists(m_root)) Directory.Delete(m_root, true);
        }

        private ApiUsageReport Scan()
        {
            var options = new ApiUsageOptions { ScanAssets = false, ScanPackages = false, ExcludeTests = false };
            options.CustomRoots.Add(m_root);
            return ApiUsageValidator.Scan(options);
        }

        [Test]
        public void Scan_ReportsOnlyExecutableForbiddenCalls()
        {
            File.WriteAllText(Path.Combine(m_root, "Bad.cs"),
                "class Bad { void M() { UnityEngine.Resources.Load(\"x\"); } }");
            File.WriteAllText(Path.Combine(m_root, "Allowed.cs"),
                "class Allowed { void M() { UnityEngine.Resources.Load(\"x\"); } } // yato:allow resources-load");
            File.WriteAllText(Path.Combine(m_root, "Comment.cs"),
                "class Comment { // Resources.Load(\"x\")\n }");

            ApiUsageReport report = Scan();

            Assert.AreEqual(3, report.ScannedFiles);
            Assert.AreEqual(1, report.ErrorCount, report.ToJson());
            Assert.AreEqual("Bad.cs", Path.GetFileName(report.Hits[0].Path));
            StringAssert.Contains("resources-load", report.Hits[0].RuleId);
        }

        [Test]
        public void Scan_DetectsEveryDefaultRuleWithItsSeverity()
        {
            File.WriteAllText(Path.Combine(m_root, "Mixed.cs"),
                "class Mixed {\n" +
                "  void A() { UnityEngine.SceneManagement.SceneManager.LoadScene(\"Arena\"); }\n" +
                "  void B() { UnityEngine.Time.timeScale = 0f; }\n" +
                "  void C() { UnityEditor.AssetDatabase.LoadAssetAtPath(\"a\", typeof(object)); }\n" +
                "}\n");

            ApiUsageReport report = Scan();

            Assert.AreEqual(3, report.Hits.Count, report.ToJson());
            Assert.AreEqual(2, report.ErrorCount);
            Assert.IsFalse(report.Success);
            Assert.IsTrue(report.Hits.Exists(delegate(ApiUsageHit hit) { return hit.RuleId == "scene-load-direct"; }));
            Assert.IsTrue(report.Hits.Exists(delegate(ApiUsageHit hit) { return hit.RuleId == "timescale-write"; }));
            Assert.IsTrue(report.Hits.Exists(delegate(ApiUsageHit hit) { return hit.RuleId == "assetdatabase-load"; }));
        }

        [Test]
        public void Scan_IgnoresStringsButExemptsOnlyTheNamedRule()
        {
            File.WriteAllText(Path.Combine(m_root, "Strings.cs"),
                "class Strings { void M() { System.Console.WriteLine(\"UnityEngine.Resources.Load(\\\"x\\\")\"); } }");
            File.WriteAllText(Path.Combine(m_root, "Scoped.cs"),
                "class Scoped { void M() { UnityEngine.Resources.Load(\"x\"); UnityEngine.Time.timeScale = 0f; } } // yato:allow resources-load");
            File.WriteAllText(Path.Combine(m_root, "MarkerString.cs"),
                "class MarkerString { void M() { System.Console.WriteLine(\"yato:allow timescale-write\"); UnityEngine.Time.timeScale = 0f; } }");

            ApiUsageReport report = Scan();
            Assert.AreEqual(2, report.ErrorCount, report.ToJson());
            Assert.AreEqual("timescale-write", report.Hits[0].RuleId);
            Assert.AreEqual("MarkerString.cs", Path.GetFileName(report.Hits[0].Path));
            Assert.AreEqual("Scoped.cs", Path.GetFileName(report.Hits[1].Path));
        }

        [Test]
        public void Scan_IgnoresEditorSamplesAndTestDirectories()
        {
            string editor = Path.Combine(m_root, "Editor");
            string tests = Path.Combine(m_root, "Tests");
            Directory.CreateDirectory(editor);
            Directory.CreateDirectory(tests);
            File.WriteAllText(Path.Combine(editor, "Tool.cs"), "class T { void M() { UnityEngine.Resources.Load(\"x\"); } }");
            File.WriteAllText(Path.Combine(tests, "Fixture.cs"), "class F { void M() { UnityEngine.Resources.Load(\"x\"); } }");

            var options = new ApiUsageOptions { ScanAssets = false, ScanPackages = false, ExcludeTests = true };
            options.CustomRoots.Add(m_root);
            ApiUsageReport report = ApiUsageValidator.Scan(options);

            Assert.IsTrue(report.Success, report.ToJson());
        }
    }
}
