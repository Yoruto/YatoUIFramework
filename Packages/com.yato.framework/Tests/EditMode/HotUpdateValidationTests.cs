using System;
using System.IO;
using NUnit.Framework;
using Yato.Resources;

namespace Yato.Tests.EditMode
{
    public sealed class HotUpdateValidationTests
    {
        private string m_root;
        [SetUp] public void Setup() { m_root = Path.Combine(Path.GetTempPath(), "yato-validation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(m_root); }
        [TearDown] public void Teardown() { Directory.Delete(m_root, true); }

        [TestCase("../settings/x")] [TestCase("/tmp/x")] [TestCase("C:\\x")]
        [TestCase("a\\b")] [TestCase("a/./b")] [TestCase("a//b")] [TestCase("a/../b")]
        [TestCase("")] [TestCase("CON")] [TestCase("ui/NUL.txt")] [TestCase("a:b")]
        [TestCase("ui/b.")] [TestCase("ui/%2e%2e")] [TestCase("manifest.json")]
        [TestCase("versions/x")] [TestCase("ui/a?b")]
        public void BundleName_RejectsNonPortableOrReservedPaths(string name)
        {
            Assert.Throws<ArgumentException>(() => BundleRelativePath.ValidateBundle(name));
        }

        [Test] public void NestedPath_IsAllowedAndConfined()
        {
            Assert.AreEqual(Path.Combine(m_root, "ui", "example"), BundleRelativePath.Resolve(m_root, "ui/example"));
        }

        private static string Json(string bundles, string platform = null)
        {
            return "{\"schema\":1,\"appVersion\":\"1\",\"platform\":\"" + (platform ?? RuntimePaths.PlatformName) +
                   "\",\"manifestCrc\":0,\"bundles\":{" + bundles + "}}";
        }

        [TestCase("-1", "1")] [TestCase("4294967296", "1")] [TestCase("1.5", "1")]
        [TestCase("1", "-1")] [TestCase("1", "1073741825")] [TestCase("1", "1.5")]
        public void Version_RejectsInvalidNumbers(string crc, string size)
        {
            Assert.Throws<FormatException>(() => VersionManifest.Parse(Json("\"ui/a\":{\"crc\":" + crc + ",\"size\":" + size + "}")));
        }

        [Test] public void Version_RejectsPlatformMismatch()
        {
            Assert.Throws<FormatException>(() => VersionManifest.Parse(Json("", "WrongPlatform")));
        }

        [Test] public void Version_RejectsDuplicatesAndPathCollisions()
        {
            string item = "{\"crc\":0,\"size\":0}";
            Assert.Throws<ResourceException>(() => VersionManifest.Parse(Json("\"a\":" + item + ",\"a\":" + item)));
            Assert.Throws<FormatException>(() => VersionManifest.Parse(Json("\"a\":" + item + ",\"A\":" + item)));
            Assert.Throws<FormatException>(() => VersionManifest.Parse(Json("\"a\":" + item + ",\"a/b\":" + item)));
        }

        [Test] public void Urls_RequireExplicitLocalOptIn()
        {
            var downloader = new RemoteDownloader();
            Assert.Throws<ArgumentException>(() => downloader.ValidateUrl("http://example.com/ab"));
            Assert.Throws<ArgumentException>(() => downloader.ValidateUrl(new Uri(m_root).AbsoluteUri));
            Assert.Throws<ArgumentException>(() => downloader.FileUrl("https://example.com", "../escape"));
            Assert.Throws<ArgumentException>(() => downloader.ValidateUrl("ftp://example.com"));
            Assert.Throws<ArgumentException>(() => downloader.ValidateUrl("https://user:pass@example.com"));
            downloader.AllowFileUrls = true;
            Assert.DoesNotThrow(() => downloader.ValidateUrl(new Uri(m_root).AbsoluteUri));
            Assert.AreEqual("https://example.com/Windows/ui/a", downloader.FileUrl("https://example.com", "Windows/ui/a"));
        }

        [Test] public void Storage_CommitRetainsPreviousAndCleansOnlyStaging()
        {
            var storage = new HotUpdateStorage(m_root);
            File.WriteAllText(Path.Combine(m_root, "legacy.bundle"), "legacy");
            string first = storage.Begin();
            File.WriteAllText(Path.Combine(first, "manifest.json"), "first");
            storage.Commit(first);
            string firstActive = storage.ActiveDirectory;
            string orphan = storage.Begin();
            File.WriteAllText(Path.Combine(orphan, "partial"), "partial");
            storage.CleanupStaging();
            Assert.IsFalse(Directory.Exists(orphan));
            Assert.IsTrue(File.Exists(Path.Combine(firstActive, "manifest.json")));
            string second = storage.Begin();
            File.WriteAllText(Path.Combine(second, "manifest.json"), "second");
            storage.Commit(second);
            Assert.AreNotEqual(firstActive, storage.ActiveDirectory);
            Assert.AreEqual("first", File.ReadAllText(Path.Combine(firstActive, "manifest.json")));
            Assert.AreEqual("legacy", File.ReadAllText(Path.Combine(m_root, "legacy.bundle")));
        }

        [Test] public void Storage_PointerCommitFailureLeavesPreviousFiles()
        {
            var storage = new HotUpdateStorage(m_root);
            Directory.CreateDirectory(Path.Combine(m_root, HotUpdateStorage.PointerFile));
            string staged = storage.Begin();
            File.WriteAllText(Path.Combine(staged, "manifest.json"), "candidate");
            Assert.Throws<Yato.Persistence.FileCommitException>(() => storage.Commit(staged));
            Assert.AreEqual(m_root, storage.ActiveDirectory);
            Assert.IsEmpty(Directory.GetDirectories(Path.Combine(m_root, "versions")));
        }

        [Test] public void Version_RejectsCountAndTotalSizeLimits()
        {
            var bundles = new System.Text.StringBuilder();
            for (int i = 0; i <= VersionManifest.MaxBundleCount; i++)
            {
                if (i > 0) bundles.Append(',');
                bundles.Append("\"b").Append(i).Append("\":{\"crc\":0,\"size\":0}");
            }
            Assert.Throws<FormatException>(() => VersionManifest.Parse(Json(bundles.ToString())));
            bundles.Clear();
            for (int i = 0; i < 9; i++)
            {
                if (i > 0) bundles.Append(',');
                bundles.Append("\"b").Append(i).Append("\":{\"crc\":0,\"size\":1073741824}");
            }
            Assert.Throws<FormatException>(() => VersionManifest.Parse(Json(bundles.ToString())));
        }

        [Test] public void Path_RejectsJunctionAtChildAndAtRoot()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Junction test requires Windows");
            string outside = Path.Combine(Path.GetTempPath(), "yato-outside-" + Guid.NewGuid().ToString("N"));
            string junction = Path.Combine(m_root, "linked");
            Directory.CreateDirectory(outside);
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + junction + "\" \"" + outside + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    Assert.AreEqual(0, process.ExitCode, "Could not create test junction");
                }
                Assert.Throws<IOException>(() => BundleRelativePath.Resolve(m_root, "linked/escape"));
                Assert.Throws<IOException>(() => new HotUpdateStorage(junction));
                Assert.IsEmpty(Directory.GetFiles(outside));
            }
            finally
            {
                if (Directory.Exists(junction)) Directory.Delete(junction);
                Directory.Delete(outside);
            }
        }

        [Test] public void Crc_StreamsWithSameResultAsBuffer()
        {
            var data = new byte[170000];
            new Random(7).NextBytes(data);
            string path = Path.Combine(m_root, "crc");
            File.WriteAllBytes(path, data);
            Assert.AreEqual(Crc32Util.Compute(data, 0, data.Length), Crc32Util.Compute(path));
        }
    }
}
