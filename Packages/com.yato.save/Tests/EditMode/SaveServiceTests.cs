using System;
using System.Collections;
using UnityEngine.TestTools;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yato.Persistence;

namespace Yato.Save.Tests
{
    [Serializable] public sealed class Data { public int Count; }
    public sealed class SaveServiceTests
    {
        private string m_root;
        private FileStore m_store;
        [SetUp] public void Setup() { m_root = Path.Combine(Path.GetTempPath(), "yato-save-" + Guid.NewGuid().ToString("N")); m_store = new FileStore(m_root); }
        [TearDown] public void Teardown() { if (Directory.Exists(m_root)) Directory.Delete(m_root, true); }
        private SaveService<Data> Service(int version = 1, ISaveMigrator<Data> migrator = null)
            => new SaveService<Data>(m_store, version, data => data.Count < 0 ? "Negative count" : null, migrator);

        [UnityTest] public IEnumerator RoundTripBackupAndExplicitRecovery()
        {
            var task = RoundTripBackupAndExplicitRecoveryRun();
            while (!task.IsCompleted) yield return null;
            task.GetAwaiter().GetResult();
        }
        private async Task RoundTripBackupAndExplicitRecoveryRun()
        {
            var saves = Service();
            Assert.AreEqual(SaveLoadStatus.Missing, (await saves.LoadAsync("a")).Status);
            await saves.SaveAsync("a", new Data { Count = 1 });
            await saves.SaveAsync("a", new Data { Count = 2 });
            Assert.AreEqual(2, (await saves.LoadAsync("a")).Data.Count);
            m_store.WriteAllText("saves/slot-a/primary.json", "corrupt");
            var corrupt = await saves.LoadAsync("a");
            Assert.AreEqual(SaveLoadStatus.Corrupt, corrupt.Status);
            Assert.IsTrue(corrupt.BackupAvailable);
            Assert.AreEqual("corrupt", m_store.ReadAllText("saves/slot-a/primary.json"));
            await Expect<InvalidDataException>(async () => await saves.SaveAsync("a", new Data { Count = 3 }));
            var recovered = await saves.RecoverBackupAsync("a");
            Assert.AreEqual(1, recovered.Data.Count);
            Assert.AreEqual(1, (await saves.LoadAsync("a")).Data.Count);
            Assert.AreEqual(1, (await saves.ListAsync()).Count);
            await saves.DeleteAsync("a");
            Assert.AreEqual(SaveLoadStatus.Missing, (await saves.LoadAsync("a")).Status);
        }

        private sealed class Migrator : ISaveMigrator<Data>
        {
            public Data Migrate(int from, int to, string payload)
            {
                var data = new JsonUtilitySerializer().Deserialize<Data>(payload); data.Count += 10; return data;
            }
        }
        private sealed class BrokenMigrator : ISaveMigrator<Data>
        {
            public Data Migrate(int from, int to, string payload) { throw new InvalidOperationException("Migration failed"); }
        }
        [UnityTest] public IEnumerator Migration_IsInMemoryAndNewerDataIsRejected()
        {
            var task = Migration_IsInMemoryAndNewerDataIsRejectedRun();
            while (!task.IsCompleted) yield return null;
            task.GetAwaiter().GetResult();
        }
        private async Task Migration_IsInMemoryAndNewerDataIsRejectedRun()
        {
            await Service().SaveAsync("a", new Data { Count = 1 });
            string before = m_store.ReadAllText("saves/slot-a/primary.json");
            var migrated = await Service(2, new Migrator()).LoadAsync("a");
            Assert.AreEqual(SaveLoadStatus.Migrated, migrated.Status);
            Assert.AreEqual(11, migrated.Data.Count);
            Assert.AreEqual(before, m_store.ReadAllText("saves/slot-a/primary.json"));
            Assert.AreEqual(SaveLoadStatus.Corrupt, (await Service(2, new BrokenMigrator()).LoadAsync("a")).Status);
            await Service(2, new Migrator()).SaveAsync("a", migrated.Data);
            Assert.AreEqual(SaveLoadStatus.Incompatible, (await Service().LoadAsync("a")).Status);
        }

        [UnityTest] public IEnumerator InvalidDataCancellationAndCommitFailure_PreservePrimary()
        {
            var task = InvalidDataCancellationAndCommitFailure_PreservePrimaryRun();
            while (!task.IsCompleted) yield return null;
            task.GetAwaiter().GetResult();
        }
        private async Task InvalidDataCancellationAndCommitFailure_PreservePrimaryRun()
        {
            var saves = Service();
            await saves.SaveAsync("a", new Data { Count = 4 });
            await Expect<InvalidDataException>(async () => await saves.SaveAsync("a", new Data { Count = -1 }));
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Expect<OperationCanceledException>(async () => await saves.SaveAsync("a", new Data { Count = 5 }, cts.Token));
            }
            string path = Path.Combine(m_root, "saves/slot-a/primary.json");
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                await Expect<FileCommitException>(async () => await saves.SaveAsync("a", new Data { Count = 8 }));
            Assert.AreEqual(4, (await saves.LoadAsync("a")).Data.Count);
            Assert.AreEqual(4, (await saves.RecoverBackupAsync("a")).Data.Count);
        }

        [TestCase("../escape")] [TestCase("a/b")] [TestCase("")]
        public void SlotPaths_RejectTraversal(string id)
        {
            Assert.ThrowsAsync<ArgumentException>(async () => await Service().SaveAsync(id, new Data()));
        }

        [UnityTest] public IEnumerator SameSlotAcrossServices_IsBusyWhileOtherSlotCanProceed()
        {
            var task = SameSlotAcrossServices_IsBusyWhileOtherSlotCanProceedRun();
            while (!task.IsCompleted) yield return null;
            task.GetAwaiter().GetResult();
        }
        private async Task SameSlotAcrossServices_IsBusyWhileOtherSlotCanProceedRun()
        {
            await Service().SaveAsync("a", new Data { Count = 1 });
            var heldStore = new HeldStore(m_store);
            var first = new SaveService<Data>(heldStore, 1).LoadAsync("a");
            await Expect<FileBusyException>(async () => await Service().LoadAsync("a"));
            await Service().SaveAsync("b", new Data { Count = 2 });
            heldStore.Read.SetResult(m_store.ReadAllText("saves/slot-a/primary.json"));
            Assert.AreEqual(1, (await first).Data.Count);
        }

        private static async Task Expect<TError>(Func<Task> operation) where TError : Exception
        {
            try { await operation(); } catch (TError) { return; }
            Assert.Fail("Expected " + typeof(TError).Name);
        }

        private sealed class HeldStore : IFileStore
        {
            private readonly IFileStore m_inner;
            public TaskCompletionSource<string> Read = new TaskCompletionSource<string>();
            public HeldStore(IFileStore inner) { m_inner = inner; }
            public string RootPath => m_inner.RootPath;
            public bool Exists(string p) => m_inner.Exists(p);
            public long GetFileSize(string p) => m_inner.GetFileSize(p);
            public DateTime GetLastWriteTimeUtc(string p) => m_inner.GetLastWriteTimeUtc(p);
            public byte[] ReadAllBytes(string p) => m_inner.ReadAllBytes(p);
            public string ReadAllText(string p) => m_inner.ReadAllText(p);
            public void WriteAllBytes(string p, byte[] d, CancellationToken ct = default) => m_inner.WriteAllBytes(p, d, ct);
            public void WriteAllText(string p, string d, CancellationToken ct = default) => m_inner.WriteAllText(p, d, ct);
            public Task<byte[]> ReadAllBytesAsync(string p, CancellationToken ct = default) => m_inner.ReadAllBytesAsync(p, ct);
            public Task<string> ReadAllTextAsync(string p, CancellationToken ct = default) => p.EndsWith("primary.json") ? Read.Task : m_inner.ReadAllTextAsync(p, ct);
            public Task WriteAllBytesAsync(string p, byte[] d, CancellationToken ct = default) => m_inner.WriteAllBytesAsync(p, d, ct);
            public Task WriteAllTextAsync(string p, string d, CancellationToken ct = default) => m_inner.WriteAllTextAsync(p, d, ct);
            public bool Delete(string p) => m_inner.Delete(p);
            public void EnsureDirectory(string p) => m_inner.EnsureDirectory(p);
            public System.Collections.Generic.IReadOnlyList<string> EnumerateFiles(string p, string f = "*", bool r = false) => m_inner.EnumerateFiles(p, f, r);
            public IDisposable AcquireLock(string p) => m_inner.AcquireLock(p);
        }
    }
}
