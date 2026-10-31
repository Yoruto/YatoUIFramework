using System;
using System.Collections;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.Resources;

namespace Yato.Tests.PlayMode
{
    // Exercises real file:// downloads and transactions without requiring real AssetBundles.
    // Real AB loading remains covered separately by HotUpdateSmokeTests.
    public sealed class HotUpdateTransactionTests
    {
        private string m_root, m_remote, m_local, m_url;
        private HotUpdateService m_service;
        private FileStream m_promotionLock;
        private bool m_lockNextPromotion;
        [SetUp] public void Setup()
        {
            m_root = Path.Combine(Application.temporaryCachePath, "yato-transaction-" + Guid.NewGuid().ToString("N"));
            m_remote = Path.Combine(m_root, "remote", RuntimePaths.PlatformName);
            m_local = Path.Combine(m_root, "cache");
            Directory.CreateDirectory(m_remote);
            m_url = new Uri(Path.GetDirectoryName(m_remote)).AbsoluteUri;
            m_service = new HotUpdateService(new RemoteDownloader { AllowFileUrls = true, MaxRetry = 1 }, m_local);
            Publish("1", "first");
        }
        [TearDown] public void Teardown() { m_promotionLock?.Dispose(); m_promotionLock = null; if (Directory.Exists(m_root)) Directory.Delete(m_root, true); }

        private void Publish(string version, string content)
        {
            Directory.CreateDirectory(Path.Combine(m_remote, "ui"));
            string bundle = Path.Combine(m_remote, "ui", "example");
            File.WriteAllText(bundle, content);
            long size = new FileInfo(bundle).Length;
            uint crc = Crc32Util.Compute(bundle);
            string info = "{\"crc\":" + crc + ",\"size\":" + size + "}";
            string manifest = "{\"schema\":2,\"version\":\"" + version + "\",\"platform\":\"" +
                RuntimePaths.PlatformName + "\",\"bundles\":{\"ui/example\":" + info + "},\"assets\":{}}";
            File.WriteAllText(Path.Combine(m_remote, "manifest.json"), manifest);
            File.WriteAllText(Path.Combine(m_remote, "version.json"), "{\"schema\":1,\"appVersion\":\"" +
                version + "\",\"platform\":\"" + RuntimePaths.PlatformName + "\",\"manifestCrc\":" +
                Crc32Util.Compute(Path.Combine(m_remote, "manifest.json")) + ",\"bundles\":{\"ui/example\":" + info + "}}");
        }

        [UnityTest] public IEnumerator CommitRepeatAndSwitch_PreservePinnedFiles()
        {
            var first = m_service.RunAsync(m_url);
            yield return first.AsEnumerator();
            Assert.IsTrue(first.GetResult().Success, first.GetResult().Error);
            string pinned = new HotUpdateStorage(m_local).ActiveDirectory;
            var repeat = m_service.RunAsync(m_url);
            yield return repeat.AsEnumerator();
            Assert.IsTrue(repeat.GetResult().Success, repeat.GetResult().Error);
            Assert.IsFalse(repeat.GetResult().Updated);
            Assert.AreEqual(1, repeat.GetResult().SkippedBundles);
            Assert.AreEqual(pinned, new HotUpdateStorage(m_local).ActiveDirectory);
            Publish("2", "second");
            var update = m_service.RunAsync(m_url);
            yield return update.AsEnumerator();
            Assert.IsTrue(update.GetResult().Success, update.GetResult().Error);
            Assert.AreEqual("first", File.ReadAllText(BundleRelativePath.Resolve(pinned, "ui/example")));
            Assert.AreEqual("second", File.ReadAllText(BundleRelativePath.Resolve(new HotUpdateStorage(m_local).ActiveDirectory, "ui/example")));
        }

        [UnityTest] public IEnumerator CorruptionAndInvalidManifest_RetainLastGood()
        {
            var first = m_service.RunAsync(m_url);
            yield return first.AsEnumerator();
            Assert.IsTrue(first.GetResult().Success, first.GetResult().Error);
            string active = new HotUpdateStorage(m_local).ActiveDirectory;
            Publish("2", "next");
            File.WriteAllText(Path.Combine(m_remote, "ui", "example"), "bad!");
            var broken = m_service.RunAsync(m_url);
            yield return broken.AsEnumerator();
            Assert.IsFalse(broken.GetResult().Success);
            Assert.AreEqual(active, new HotUpdateStorage(m_local).ActiveDirectory);
            Publish("3", "third");
            File.WriteAllText(Path.Combine(m_remote, "manifest.json"), "{}");
            var badManifest = m_service.RunAsync(m_url);
            yield return badManifest.AsEnumerator();
            Assert.IsFalse(badManifest.GetResult().Success);
            Assert.AreEqual(active, new HotUpdateStorage(m_local).ActiveDirectory);
            Assert.AreEqual("first", File.ReadAllText(BundleRelativePath.Resolve(active, "ui/example")));
            Assert.IsEmpty(Directory.GetDirectories(Path.Combine(m_local, "staging")));
        }

        [UnityTest] public IEnumerator BusyAndPrecommitCancellation_RetainLastGood()
        {
            var first = m_service.RunAsync(m_url);
            yield return first.AsEnumerator();
            string active = new HotUpdateStorage(m_local).ActiveDirectory;
            Publish("2", "second");
            using (var cts = new CancellationTokenSource())
            {
                var updating = m_service.RunAsync(m_url, new CancelProgress(cts), cts.Token);
                var busy = m_service.RunAsync(m_url);
                Assert.IsTrue(busy.GetResult().Busy);
                Assert.IsTrue(HotUpdateMaintenance.PrepareStartup(m_local).Busy);
                yield return updating.AsEnumerator();
                Assert.IsTrue(updating.GetResult().Canceled, updating.GetResult().Error);
                Assert.AreEqual(active, new HotUpdateStorage(m_local).ActiveDirectory);
            }
        }

        [UnityTest] public IEnumerator OversizedAndWrongLengthDownloads_DoNotReplaceDestination()
        {
            string target = Path.Combine(m_root, "destination");
            File.WriteAllText(target, "old");
            var downloader = new RemoteDownloader { AllowFileUrls = true, MaxRetry = 1 };
            var oversized = downloader.DownloadAsync(new Uri(Path.Combine(m_remote, "ui", "example")).AbsoluteUri, target, maxBytes: 2);
            yield return oversized.AsEnumerator();
            Assert.Catch<Exception>(() => oversized.GetResult());
            Assert.AreEqual("old", File.ReadAllText(target));
            var wrongLength = downloader.DownloadAsync(new Uri(Path.Combine(m_remote, "ui", "example")).AbsoluteUri, target, expectedSize: 1);
            yield return wrongLength.AsEnumerator();
            Assert.Catch<Exception>(() => wrongLength.GetResult());
            Assert.AreEqual("old", File.ReadAllText(target));
            Assert.IsEmpty(Directory.GetFiles(m_root, "*.part", SearchOption.AllDirectories));
        }

        [UnityTest] public IEnumerator PointerFailureAndOrphanRecovery_DoNotLosePreviousVersion()
        {
            Directory.CreateDirectory(m_local);
            Directory.CreateDirectory(Path.Combine(m_local, HotUpdateStorage.PointerFile));
            var failing = m_service.RunAsync(m_url);
            yield return failing.AsEnumerator();
            Assert.IsFalse(failing.GetResult().Success);
            Assert.IsEmpty(Directory.GetDirectories(Path.Combine(m_local, "versions")));
            Directory.Delete(Path.Combine(m_local, HotUpdateStorage.PointerFile));
            var storage = new HotUpdateStorage(m_local);
            string orphan = storage.Begin();
            File.WriteAllText(Path.Combine(orphan, "partial"), "partial");
            var recovered = m_service.RunAsync(m_url);
            yield return recovered.AsEnumerator();
            Assert.IsTrue(recovered.GetResult().Success, recovered.GetResult().Error);
            Assert.IsFalse(Directory.Exists(orphan));
        }

        [UnityTest] public IEnumerator LockedExistingPointer_CommitFailurePreservesLastGood()
        {
            var first = m_service.RunAsync(m_url);
            yield return first.AsEnumerator();
            Assert.IsTrue(first.GetResult().Success, first.GetResult().Error);
            string active = new HotUpdateStorage(m_local).ActiveDirectory;
            string pointer = Path.Combine(m_local, HotUpdateStorage.PointerFile);
            Publish("2", "second");
            using (var held = new FileStream(pointer, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var operation = m_service.RunAsync(m_url);
                yield return operation.AsEnumerator();
                Assert.IsFalse(operation.GetResult().Success);
            }
            Assert.AreEqual(active, new HotUpdateStorage(m_local).ActiveDirectory);
            Assert.AreEqual("first", File.ReadAllText(BundleRelativePath.Resolve(active, "ui/example")));
            Assert.AreEqual(1, Directory.GetDirectories(Path.Combine(m_local, "versions")).Length);
        }

        [UnityTest] public IEnumerator CancellationAfterCommit_ReportsSuccess()
        {
            using (var cts = new CancellationTokenSource())
            {
                int reports = 0;
                var operation = m_service.RunAsync(m_url, new InlineProgress(_ => { if (++reports == 2) cts.Cancel(); }), cts.Token);
                yield return operation.AsEnumerator();
                Assert.IsTrue(cts.IsCancellationRequested);
                Assert.IsTrue(operation.GetResult().Success, operation.GetResult().Error);
                Assert.IsFalse(operation.GetResult().Canceled);
                Assert.IsTrue(File.Exists(Path.Combine(new HotUpdateStorage(m_local).ActiveDirectory, "manifest.json")));
            }
        }

        [UnityTest] public IEnumerator ValidCrcWithInvalidDependencyGraph_IsRejectedBeforeCommit()
        {
            string path = Path.Combine(m_remote, "manifest.json");
            string invalid = File.ReadAllText(path).Replace("\"size\":5", "\"size\":5,\"deps\":[\"missing\"]");
            File.WriteAllText(path, invalid);
            string versionPath = Path.Combine(m_remote, "version.json");
            string version = File.ReadAllText(versionPath);
            uint crc = Crc32Util.Compute(path);
            var json = System.Text.RegularExpressions.Regex.Replace(version, "\"manifestCrc\":[0-9]+", "\"manifestCrc\":" + crc);
            File.WriteAllText(versionPath, json);
            var operation = m_service.RunAsync(m_url);
            yield return operation.AsEnumerator();
            Assert.IsFalse(operation.GetResult().Success);
            StringAssert.Contains("missing", operation.GetResult().Error);
            Assert.IsFalse(File.Exists(Path.Combine(m_local, HotUpdateStorage.PointerFile)));
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        // A non-delete-shared child file deterministically reproduces Windows rename denial.
        private void LockStagedBundle(float value)
        {
            if (!m_lockNextPromotion || value < 1f) return;
            string stage = Directory.GetDirectories(Path.Combine(m_local, "staging"))[0];
            m_promotionLock = new FileStream(Path.Combine(stage, "ui", "example"), FileMode.Open, FileAccess.Read, FileShare.Read);
            m_lockNextPromotion = false;
        }

        [UnityTest] public IEnumerator TransientPromotionLock_YieldsThenCommits()
        {
            var initial = m_service.RunAsync(m_url);
            yield return initial.AsEnumerator();
            Assert.IsTrue(initial.GetResult().Success, initial.GetResult().Error);
            string active = new HotUpdateStorage(m_local).ActiveDirectory;
            Publish("2", "second");
            m_lockNextPromotion = true;
            var update = m_service.RunAsync(m_url, new InlineProgress(LockStagedBundle));
            while (m_promotionLock == null && !update.IsDone) yield return null;
            Assert.IsNotNull(m_promotionLock);
            float until = Time.realtimeSinceStartup + 0.12f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.IsFalse(update.IsDone, "Promotion must yield while the child file is held");
            Assert.AreEqual(active, new HotUpdateStorage(m_local).ActiveDirectory);
            Assert.IsTrue(m_service.RunAsync(m_url).GetResult().Busy);
            m_promotionLock.Dispose(); m_promotionLock = null;
            yield return update.AsEnumerator();
            Assert.IsTrue(update.GetResult().Success, update.GetResult().Error);
            Assert.AreNotEqual(active, new HotUpdateStorage(m_local).ActiveDirectory);
            Assert.AreEqual("first", File.ReadAllText(BundleRelativePath.Resolve(active, "ui/example")));
        }

        [UnityTest] public IEnumerator PersistentPromotionLock_TimesOutAndPreservesPointer()
        {
            var initial = m_service.RunAsync(m_url);
            yield return initial.AsEnumerator();
            Assert.IsTrue(initial.GetResult().Success, initial.GetResult().Error);
            string active = new HotUpdateStorage(m_local).ActiveDirectory;
            Publish("2", "second");
            m_lockNextPromotion = true;
            var update = m_service.RunAsync(m_url, new InlineProgress(LockStagedBundle));
            yield return update.AsEnumerator();
            Assert.IsNotNull(m_promotionLock);
            Assert.IsFalse(update.GetResult().Success);
            StringAssert.Contains("Snapshot promotion remained unavailable", update.GetResult().Error);
            Assert.AreEqual(active, new HotUpdateStorage(m_local).ActiveDirectory);
            Assert.AreEqual(1, Directory.GetDirectories(Path.Combine(m_local, "versions")).Length);
            m_promotionLock.Dispose(); m_promotionLock = null;
            var retry = m_service.RunAsync(m_url);
            yield return retry.AsEnumerator();
            Assert.IsTrue(retry.GetResult().Success, retry.GetResult().Error);
            Assert.IsEmpty(Directory.GetDirectories(Path.Combine(m_local, "staging")));
        }

        [UnityTest] public IEnumerator CanceledPromotionRetry_PreservesPointer()
        {
            var initial = m_service.RunAsync(m_url);
            yield return initial.AsEnumerator();
            Assert.IsTrue(initial.GetResult().Success, initial.GetResult().Error);
            string active = new HotUpdateStorage(m_local).ActiveDirectory;
            Publish("2", "second");
            using (var cancellation = new CancellationTokenSource())
            {
                m_lockNextPromotion = true;
                var update = m_service.RunAsync(m_url, new InlineProgress(LockStagedBundle), cancellation.Token);
                while (m_promotionLock == null && !update.IsDone) yield return null;
                Assert.IsNotNull(m_promotionLock);
                cancellation.Cancel();
                yield return update.AsEnumerator();
                Assert.IsTrue(update.GetResult().Canceled, update.GetResult().Error);
                Assert.AreEqual(active, new HotUpdateStorage(m_local).ActiveDirectory);
                m_promotionLock.Dispose(); m_promotionLock = null;
            }
            var retry = m_service.RunAsync(m_url);
            yield return retry.AsEnumerator();
            Assert.IsTrue(retry.GetResult().Success, retry.GetResult().Error);
        }
#endif

        private sealed class InlineProgress : IProgress<float>
        {
            private readonly Action<float> m_report;
            public InlineProgress(Action<float> report) { m_report = report; }
            public void Report(float value) { m_report(value); }
        }

        private sealed class CancelProgress : IProgress<float>
        {
            private readonly CancellationTokenSource m_cts;
            public CancelProgress(CancellationTokenSource cts) { m_cts = cts; }
            public void Report(float value) { m_cts.Cancel(); }
        }
    }
}
