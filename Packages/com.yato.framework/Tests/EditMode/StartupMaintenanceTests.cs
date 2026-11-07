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
        private IBundleCachePolicy m_previousPolicy;
        [SetUp] public void Setup()
        {
            m_previousPolicy = BundleCachePolicy.Current;
            HotUpdateBundleCachePolicy.Install();
            m_root = Path.Combine(Path.GetTempPath(), "yato-maintenance-" + Guid.NewGuid().ToString("N"));
        }
        [TearDown] public void Teardown()
        {
            BundleCachePolicy.Current = m_previousPolicy;
            if (Directory.Exists(m_root)) Directory.Delete(m_root, true);
        }
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
            string unmanaged = Path.Combine(m_root, "versions", "user-cache");
            Directory.CreateDirectory(unmanaged);
            File.WriteAllText(Path.Combine(unmanaged, "keep"), "keep");
            var result = HotUpdateMaintenance.PrepareStartup(m_root);
            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(1, result.DeletedDirectories);
            Assert.AreEqual(5, result.DeletedBytes);
            Assert.AreEqual(2, result.RetainedDirectories);
            Assert.AreEqual(1, result.SkippedDirectories);
            Assert.IsTrue(File.Exists(Path.Combine(unmanaged, "keep")));
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
        [Test] public void Cleanup_TrailingSeparatorsDoNotBypassInUseProtection()
        {
            var storage = new HotUpdateStorage(m_root);
            string active = Commit(storage, "first");
            var loader = new BundleLoader(new BundleRefCounter(_ => { }), active + Path.DirectorySeparatorChar);
            var result = HotUpdateMaintenance.PrepareStartup(m_root + Path.DirectorySeparatorChar);
            Assert.IsTrue(result.InUse);
            Assert.AreEqual(0, result.DeletedDirectories);
        }

        [Test] public void Cleanup_MissingPointerRetainsUnknownSnapshots()
        {
            var storage = new HotUpdateStorage(m_root);
            string active = Commit(storage, "first");
            File.Delete(Path.Combine(m_root, "active.txt"));
            var result = HotUpdateMaintenance.PrepareStartup(m_root);
            Assert.IsFalse(result.Success);
            Assert.IsTrue(Directory.Exists(active));
        }

        [Test] public void Cleanup_InvalidPreviousPointerRetainsAllVersions()
        {
            var storage = new HotUpdateStorage(m_root);
            string first = Commit(storage, "first");
            Commit(storage, "second");
            string current = Commit(storage, "third");
            string pointer = Path.Combine(m_root, "active.txt");
            string invalid = Path.GetFileName(current) + "\n" + Path.GetTempPath();
            File.WriteAllText(pointer, invalid);
            var result = HotUpdateMaintenance.PrepareStartup(m_root);
            Assert.IsFalse(result.Success);
            StringAssert.Contains("Invalid previous", result.Error);
            Assert.IsTrue(Directory.Exists(first));
            Assert.AreEqual(invalid, File.ReadAllText(pointer));
        }

#if UNITY_EDITOR_WIN
        [Test] public void Cleanup_LockedOldFileReportsFailureAndCanRetry()
        {
            var storage = new HotUpdateStorage(m_root);
            string first = Commit(storage, "first");
            Commit(storage, "second");
            Commit(storage, "third");
            string pointer = File.ReadAllText(Path.Combine(m_root, "active.txt"));
            using (var held = new FileStream(Path.Combine(first, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = HotUpdateMaintenance.PrepareStartup(m_root);
                Assert.IsFalse(result.Success);
                Assert.IsNotEmpty(result.Error);
                Assert.AreEqual(0, result.DeletedDirectories);
                Assert.IsTrue(Directory.Exists(first));
                Assert.AreEqual(pointer, File.ReadAllText(Path.Combine(m_root, "active.txt")));
            }
            Assert.IsTrue(HotUpdateMaintenance.PrepareStartup(m_root).Success);
            Assert.IsFalse(Directory.Exists(first));
        }

        [Test] public void Cleanup_LinkPreflightRetainsCandidatesAndOutsideData()
        {
            var storage = new HotUpdateStorage(m_root);
            string first = Commit(storage, "first");
            string second = Commit(storage, "second");
            Commit(storage, "third");
            Commit(storage, "fourth");
            string outside = Path.Combine(Path.GetTempPath(), "yato-cleanup-outside-" + Guid.NewGuid().ToString("N"));
            string junction = Path.Combine(second, "linked");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "keep"), "untouched");
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + junction + "\" \"" + outside + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    process.StandardOutput.ReadToEnd(); process.WaitForExit();
                    Assert.AreEqual(0, process.ExitCode);
                }
                string pointer = File.ReadAllText(Path.Combine(m_root, "active.txt"));
                var result = HotUpdateMaintenance.PrepareStartup(m_root);
                Assert.IsFalse(result.Success);
                Assert.AreEqual(0, result.DeletedDirectories);
                Assert.IsTrue(Directory.Exists(first));
                Assert.IsTrue(Directory.Exists(second));
                Assert.AreEqual("untouched", File.ReadAllText(Path.Combine(outside, "keep")));
                Assert.AreEqual(pointer, File.ReadAllText(Path.Combine(m_root, "active.txt")));
            }
            finally
            {
                if (Directory.Exists(junction)) Directory.Delete(junction);
                Directory.Delete(outside, true);
            }
        }
#endif

        [Test] public void BuildResult_RequiresExplicitSuccess()
        {
            var result = new BundleBuildResult();
            Assert.DoesNotThrow(() => BuildPipelineEntry.RequireSuccess(result));
            result.Errors.Add("invalid manifest");
            StringAssert.Contains("invalid manifest", Assert.Throws<InvalidOperationException>(() => BuildPipelineEntry.RequireSuccess(result)).Message);
        }
    }
}
