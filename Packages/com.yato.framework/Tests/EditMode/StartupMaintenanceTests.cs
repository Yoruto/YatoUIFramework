using System;
using System.IO;
using NUnit.Framework;
using Yato.Resources;
using Yato.Editor;

namespace Yato.Tests.EditMode
{
    public sealed class StartupMaintenanceTests
    {
        private string m_root;
        [SetUp] public void Setup() { m_root = Path.Combine(Path.GetTempPath(), "yato-maintenance-" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void Teardown() { if (Directory.Exists(m_root)) Directory.Delete(m_root, true); }
        private string Commit(HotUpdateStorage storage, string content)
        {
            string stage = storage.Begin();
            File.WriteAllText(Path.Combine(stage, "manifest.json"), content);
            storage.Commit(stage);
            return storage.ActiveDirectory;
        }
        [Test] public void Cleanup_RetainsCurrentPreviousAndLegacy()
        {
            var storage = new HotUpdateStorage(m_root);
            string first = Commit(storage, "first");
            string second = Commit(storage, "second");
            string third = Commit(storage, "third");
            File.WriteAllText(Path.Combine(m_root, "legacy"), "keep");
            var result = HotUpdateMaintenance.PrepareStartup(m_root);
            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(1, result.DeletedDirectories);
            Assert.IsFalse(Directory.Exists(first));
            Assert.IsTrue(Directory.Exists(second));
            Assert.IsTrue(Directory.Exists(third));
            Assert.IsTrue(File.Exists(Path.Combine(m_root, "legacy")));
        }
        [Test] public void Cleanup_InvalidPointerDeletesNothing()
        {
            var storage = new HotUpdateStorage(m_root);
            string first = Commit(storage, "first");
            File.WriteAllText(Path.Combine(m_root, "active.txt"), "broken");
            var result = HotUpdateMaintenance.PrepareStartup(m_root);
            Assert.IsFalse(result.Success);
            Assert.IsTrue(Directory.Exists(first));
            Assert.AreEqual("broken", File.ReadAllText(Path.Combine(m_root, "active.txt")));
        }
        [Test] public void Cleanup_RefusesAfterLoaderCreated()
        {
            Directory.CreateDirectory(m_root);
            var loader = new BundleLoader(new BundleRefCounter(_ => { }), m_root);
            var result = HotUpdateMaintenance.PrepareStartup(m_root);
            Assert.IsTrue(result.InUse);
            Assert.AreEqual(0, result.DeletedDirectories);
        }
        [Test] public void BuildResult_RequiresExplicitSuccess()
        {
            var result = new BundleBuildResult();
            Assert.DoesNotThrow(() => BuildPipelineEntry.RequireSuccess(result));
            result.Errors.Add("invalid manifest");
            StringAssert.Contains("invalid manifest", Assert.Throws<InvalidOperationException>(() => BuildPipelineEntry.RequireSuccess(result)).Message);
        }
    }
}
