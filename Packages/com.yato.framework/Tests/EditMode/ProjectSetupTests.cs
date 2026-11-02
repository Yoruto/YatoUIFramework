using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Yato.Editor;
using Yato.UI;

namespace Yato.Tests.EditMode
{
    public sealed class ProjectSetupTests
    {
        private string m_folder;
        [SetUp] public void Setup() { m_folder = "Assets/YatoSetupTest_" + Guid.NewGuid().ToString("N"); }
        [TearDown] public void Teardown() { AssetDatabase.DeleteAsset(m_folder); }
        [Test] public void StarterHasValidCatalogAndCannotOverwriteAnExistingFolder()
        {
            var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool dirty = original.isDirty;
            var roots = original.GetRootGameObjects();
            string scene = ProjectSetupWizard.Create(m_folder);
            Assert.AreEqual(original, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Assert.AreEqual(dirty, original.isDirty);
            CollectionAssert.AreEquivalent(roots, original.GetRootGameObjects());
            var preview = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(scene);
            try { Assert.IsNotNull(preview.GetRootGameObjects().Single().GetComponent<UIStarter>()); }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
            Assert.IsTrue(File.Exists(scene));
            var catalog = AssetDatabase.LoadAssetAtPath<UIPageCatalog>(m_folder + "/Pages.asset");
            Assert.IsEmpty(catalog.Validate());
            Assert.IsTrue(catalog.Get("Main").IsValid);
            Assert.IsFalse(ProjectDoctor.Check(m_folder, false).Any(x => x.IsError));
            string before = File.ReadAllText(scene);
            Assert.Throws<IOException>(() => ProjectSetupWizard.Create(m_folder));
            Assert.AreEqual(before, File.ReadAllText(scene));
        }
        [Test] public void DoctorLocatesInvalidPageRegistrationAndMissingPrefab()
        {
            ProjectSetupWizard.Create(m_folder);
            var catalog = AssetDatabase.LoadAssetAtPath<UIPageCatalog>(m_folder + "/Pages.asset");
            catalog.Pages = new[] {
                new UIPageRegistration { PageId = "duplicate", ResourceKey = "missing/prefab" },
                new UIPageRegistration { PageId = "duplicate", ResourceKey = "missing/other" } };
            var issues = ProjectDoctor.Check(m_folder, false);
            Assert.IsTrue(issues.Any(x => x.Path.EndsWith("Pages.asset") && x.Message.Contains("Duplicate page ID")));
            Assert.IsTrue(issues.Any(x => x.Message.Contains("Resources prefab not found")));
        }
        [TestCase("../Outside")] [TestCase("Assets/../Outside")] [TestCase("Packages/Starter")]
        public void SetupRejectsFoldersOutsideAssets(string folder) => Assert.Throws<ArgumentException>(() => ProjectSetupWizard.Create(folder));
    }
}
