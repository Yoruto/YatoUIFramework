using System;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Yato.Resources;

namespace Yato.Tests.EditMode
{
    /// <summary>
    /// Recovery guarantees for interrupted updates: an abandoned gate lease, a damaged active
    /// pointer and optional SHA-256 provenance must not make the last successful snapshot unusable.
    /// </summary>
    public sealed class HotUpdateRecoveryTests
    {
        private string m_root;

        [SetUp]
        public void Setup()
        {
            HotUpdateGate.Reset();
            m_root = Path.Combine(Path.GetTempPath(), "yato-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
        }

        [TearDown]
        public void Teardown()
        {
            HotUpdateGate.Reset();
            if (Directory.Exists(m_root)) Directory.Delete(m_root, true);
        }

        private string Commit(HotUpdateStorage storage, string content)
        {
            string stage = storage.Begin();
            File.WriteAllText(Path.Combine(stage, "manifest.json"), content);
            storage.Commit(stage);
            return storage.ActiveDirectory;
        }

        [Test]
        public void Gate_ReclaimsAbandonedLeaseWithoutLettingStaleOwnerReleaseIt()
        {
            int first;
            Assert.IsTrue(HotUpdateGate.TryEnter(0, out first), "first caller should acquire the gate");
            int busy;
            Assert.IsFalse(HotUpdateGate.TryEnter(0, out busy), "an active lease cannot be taken without a limit");

            Thread.Sleep(1100);
            int reclaimed;
            Assert.IsTrue(HotUpdateGate.TryEnter(1, out reclaimed), "expired lease should be reclaimed");

            HotUpdateGate.Exit(first);
            int stillBusy;
            Assert.IsFalse(HotUpdateGate.TryEnter(0, out stillBusy),
                "a late Exit from the abandoned owner must not release the newer lease");

            HotUpdateGate.Exit(reclaimed);
            int after;
            Assert.IsTrue(HotUpdateGate.TryEnter(0, out after), "gate should be free after the new owner exits");
            HotUpdateGate.Exit(after);
        }

        [Test]
        public void ActivePointer_InvalidActiveIdFallsBackToPreviousSnapshot()
        {
            var storage = new HotUpdateStorage(m_root);
            string first = Commit(storage, "first");
            Commit(storage, "second");
            File.WriteAllText(Path.Combine(m_root, HotUpdateStorage.PointerFile),
                "broken\n" + Path.GetFileName(first));

            string directory;
            string error;
            Assert.IsTrue(storage.TryGetActiveDirectory(out directory, out error));
            Assert.AreEqual(first, directory);
            StringAssert.Contains("previous", error);
            Assert.AreEqual(first, storage.ActiveDirectory);
        }

        [Test]
        public void ActivePointer_MissingActiveManifestFallsBackToPreviousSnapshot()
        {
            var storage = new HotUpdateStorage(m_root);
            string first = Commit(storage, "first");
            string second = Commit(storage, "second");
            File.Delete(Path.Combine(second, RuntimePaths.ManifestFileName));

            string directory;
            string error;
            string pointer = Path.Combine(m_root, HotUpdateStorage.PointerFile);
            Assert.IsTrue(storage.TryGetActiveDirectory(out directory, out error),
                "pointer=[" + File.ReadAllText(pointer).Replace("\n", "|") + "] error=" + error);
            Assert.AreEqual(first, directory);
            StringAssert.Contains("previous", error);
        }

        [Test]
        public void ActivePointer_DamagedPointerKeepsLegacyFlatCacheReadable()
        {
            var storage = new HotUpdateStorage(m_root);
            Commit(storage, "first");
            Commit(storage, "second");
            File.WriteAllText(Path.Combine(m_root, RuntimePaths.ManifestFileName), "legacy");
            File.WriteAllText(Path.Combine(m_root, HotUpdateStorage.PointerFile),
                "broken\n" + m_root);

            string directory;
            string error;
            Assert.IsTrue(storage.TryGetActiveDirectory(out directory, out error));
            Assert.AreEqual(m_root, directory);
            StringAssert.Contains("legacy", error);
        }

        [Test]
        public void ActivePointer_CompletelyDamagedPointerReportsFailureWithoutWriting()
        {
            var storage = new HotUpdateStorage(m_root);
            Commit(storage, "first");
            string pointer = Path.Combine(m_root, HotUpdateStorage.PointerFile);
            const string broken = "broken\nstill-broken";
            File.WriteAllText(pointer, broken);

            string directory;
            string error;
            Assert.IsFalse(storage.TryGetActiveDirectory(out directory, out error));
            Assert.IsNull(directory);
            StringAssert.Contains("Invalid active", error);
            Assert.AreEqual(broken, File.ReadAllText(pointer), "reader must never rewrite the pointer");
        }

        [Test]
        public void Integrity_Sha256IsOptionalButVerifiedWhenPresent()
        {
            string path = Path.Combine(m_root, "blob");
            byte[] data = Encoding.UTF8.GetBytes("yato-integrity");
            File.WriteAllBytes(path, data);

            uint crc;
            string sha;
            IntegrityUtil.Compute(path, true, out crc, out sha);

            Assert.AreEqual(Crc32Util.Compute(path), crc, "single-pass CRC must match the streaming implementation");
            Assert.AreEqual(64, sha.Length);
            Assert.IsTrue(IntegrityUtil.Matches(path, data.Length, crc, sha));
            Assert.IsTrue(IntegrityUtil.Matches(path, data.Length, crc, null), "legacy manifests without SHA-256 stay valid");
            Assert.IsFalse(IntegrityUtil.Matches(path, data.Length, crc, new string('0', 64)));
            Assert.IsFalse(IntegrityUtil.Matches(path, data.Length + 1, crc, null));
        }

        [Test]
        public void Version_DetectsSha256AndRejectsOversizedHashes()
        {
            string sha = new string('a', 64);
            string manifest = Json("\"ui/a\":{\"hash\":\"" + sha + "\",\"crc\":0,\"size\":0}");
            Assert.IsTrue(VersionManifest.Parse(manifest).Bundles["ui/a"].HasSha256);

            string legacy = Json("\"ui/a\":{\"crc\":0,\"size\":0}");
            Assert.IsFalse(VersionManifest.Parse(legacy).Bundles["ui/a"].HasSha256);

            string oversized = Json("\"ui/a\":{\"hash\":\"" + new string('a', 129) + "\",\"crc\":0,\"size\":0}");
            Assert.Throws<FormatException>(() => VersionManifest.Parse(oversized));
        }

        private static string Json(string bundles)
        {
            return "{\"schema\":1,\"appVersion\":\"1\",\"platform\":\"" + RuntimePaths.PlatformName +
                   "\",\"manifestCrc\":0,\"bundles\":{" + bundles + "}}";
        }
    }
}
