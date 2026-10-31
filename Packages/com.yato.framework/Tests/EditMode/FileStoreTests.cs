using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using Yato.Core;
using Yato.Persistence;

namespace Yato.Tests.EditMode
{
    /// <summary>
    /// 持久化基础设施回归（D04）：安全相对路径、同步/异步读写、
    /// 同路径互斥、临时文件安全替换与取消/失败边界。
    /// </summary>
    public sealed class FileStoreTests
    {
        private string m_root;
        private PersistencePaths m_paths;
        private FileStore m_store;

        [SetUp]
        public void SetUp()
        {
            Log.SetLogger(new SilentLogger());

            m_root = Path.Combine(Path.GetTempPath(), "yato-persistence-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);

            m_paths = new PersistencePaths(m_root);
            m_store = new FileStore(m_paths);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(m_root))
                {
                    Directory.Delete(m_root, true);
                }
            }
            catch (Exception)
            {
                // 清理失败不影响用例结论
            }

            Log.SetLogger(null);
        }

        [Test]
        public void DefaultRoot_IsPersistentDataPath()
        {
            var store = new FileStore();

            Assert.IsFalse(string.IsNullOrEmpty(store.RootPath));
            Assert.IsTrue(Path.IsPathRooted(store.RootPath));
            Assert.AreEqual(
                Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar),
                store.RootPath.TrimEnd(Path.DirectorySeparatorChar));
        }

        [Test]
        public void WriteAllText_RoundTripsUtf8WithoutBom()
        {
            m_store.WriteAllText("settings/game.json", "{\"name\":\"存档\"}");

            Assert.IsTrue(m_store.Exists("settings/game.json"));
            Assert.AreEqual("{\"name\":\"存档\"}", m_store.ReadAllText("settings/game.json"));

            byte[] raw = File.ReadAllBytes(Path.Combine(m_root, "settings", "game.json"));
            Assert.GreaterOrEqual(raw.Length, 3);
            Assert.IsFalse(raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF, "UTF-8 文本不应写 BOM");
        }

        [Test]
        public void WriteAllBytes_Overwrite_ReplacesPreviousContent()
        {
            m_store.WriteAllText("slot.txt", "old");
            m_store.WriteAllText("slot.txt", "new");

            Assert.AreEqual("new", m_store.ReadAllText("slot.txt"));
        }

        [Test]
        public void Read_MissingFile_ThrowsAndDoesNotCreate()
        {
            Assert.Throws<FileNotFoundException>(delegate { m_store.ReadAllText("missing.json"); });
            Assert.IsFalse(m_store.Exists("missing.json"));
        }

        [Test]
        public void Write_LeavesNoTemporaryFile()
        {
            m_store.WriteAllText("a/b.txt", "x");

            string[] temps = Directory.GetFiles(
                Path.Combine(m_root, "a"), "*" + FileStore.TempFileSuffix, SearchOption.AllDirectories);

            Assert.AreEqual(0, temps.Length);
        }

        [Test]
        public void Normalize_AcceptsAndCollapsesSafePaths()
        {
            Assert.AreEqual("a/b/c.txt", PersistencePaths.Normalize("a//b/./c.txt"));
            Assert.AreEqual("a/b.txt", PersistencePaths.Normalize("a\\b.txt"));
            Assert.AreEqual("folder/file.json", PersistencePaths.Normalize("  folder\\file.json  "));
        }

        [TestCase("../escape.json")]
        [TestCase("a/../../b.json")]
        [TestCase("..")]
        [TestCase("/absolute.json")]
        [TestCase("C:\\windows\\x.json")]
        [TestCase("a:b")]
        [TestCase("")]
        public void Resolve_RejectsUnsafePaths(string relativePath)
        {
            Assert.Throws<ArgumentException>(delegate { m_paths.Resolve(relativePath); });
        }

        [Test]
        public void SamePath_SecondOperation_FailsImmediatelyWithBusy()
        {
            using (IDisposable lease = m_store.AcquireLock("locked.txt"))
            {
                Assert.Throws<FileBusyException>(delegate { m_store.WriteAllText("locked.txt", "x"); });
                Assert.Throws<FileBusyException>(delegate { m_store.ReadAllText("locked.txt"); });
            }

            Assert.DoesNotThrow(delegate { m_store.WriteAllText("locked.txt", "ok"); });
        }

        [Test]
        public void DifferentPaths_AreNotBlocked()
        {
            using (m_store.AcquireLock("a.txt"))
            {
                Assert.DoesNotThrow(delegate { m_store.WriteAllText("b.txt", "b"); });
            }
        }

        [Test]
        public void Async_RoundTripsText()
        {
            m_store.WriteAllTextAsync("async/note.txt", "你好").GetAwaiter().GetResult();

            string text = m_store.ReadAllTextAsync("async/note.txt").GetAwaiter().GetResult();

            Assert.AreEqual("你好", text);
        }

        [Test]
        public void Async_PreCancelled_DoesNotCreateFile()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(delegate
            {
                m_store.WriteAllTextAsync("cancelled.txt", "x", cts.Token).GetAwaiter().GetResult();
            });

            Assert.IsFalse(m_store.Exists("cancelled.txt"));
            cts.Dispose();
        }

        [Test]
        public void Sync_PreCancelled_KeepsOriginalContent()
        {
            m_store.WriteAllText("keep.txt", "original");

            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(delegate
            {
                m_store.WriteAllText("keep.txt", "changed", cts.Token);
            });

            Assert.AreEqual("original", m_store.ReadAllText("keep.txt"));
            cts.Dispose();
        }

        [Test]
        public void Delete_ReturnsFalseForMissingFile()
        {
            Assert.IsFalse(m_store.Delete("nope.txt"));

            m_store.WriteAllText("gone.txt", "x");
            Assert.IsTrue(m_store.Delete("gone.txt"));
            Assert.IsFalse(m_store.Exists("gone.txt"));
        }

        [Test]
        public void EnumerateFiles_SkipsLeftoverTemporaryFiles()
        {
            m_store.WriteAllText("dir/a.txt", "a");
            m_store.WriteAllText("dir/b.txt", "b");
            File.WriteAllText(
                Path.Combine(m_root, "dir", "leftover" + FileStore.TempFileSuffix), "stale");

            IReadOnlyList<string> files = m_store.EnumerateFiles("dir");

            CollectionAssert.AreEqual(new[] { "dir/a.txt", "dir/b.txt" }, files);
        }

        [Test]
        public void CommitFailure_ReportsErrorAndCleansTemporaryFile()
        {
            // 目标位置被同名目录占用：提交必然失败
            Directory.CreateDirectory(Path.Combine(m_root, "blocked.txt"));

            Assert.Throws<FileCommitException>(delegate { m_store.WriteAllText("blocked.txt", "x"); });

            string[] temps = Directory.GetFiles(m_root, "*" + FileStore.TempFileSuffix, SearchOption.AllDirectories);
            Assert.AreEqual(0, temps.Length, "提交失败后必须清理临时文件");
        }

        [Test]
        public void CleanupTemporaryFiles_RemovesLeftovers()
        {
            m_store.WriteAllText("tmp/a.txt", "a");
            string stale = Path.Combine(m_root, "tmp", "stale" + FileStore.TempFileSuffix);
            File.WriteAllText(stale, "x");

            int removed = m_store.CleanupTemporaryFiles();

            Assert.AreEqual(1, removed);
            Assert.IsFalse(File.Exists(stale));
            Assert.IsTrue(m_store.Exists("tmp/a.txt"), "清理临时文件不能影响正式文件");
        }

        [Test]
        public void CleanupTemporaryFiles_RefusesWhileBusy()
        {
            using (m_store.AcquireLock("busy.txt"))
            {
                Assert.Throws<InvalidOperationException>(delegate { m_store.CleanupTemporaryFiles(); });
            }
        }

        [Test]
        public void JsonSerializer_RoundTripsAndRejectsEmptyContent()
        {
            var serializer = new JsonUtilitySerializer();
            var dto = new TestDto { Name = "测试", Value = 7 };

            string json = serializer.Serialize(dto);
            TestDto restored = serializer.Deserialize<TestDto>(json);

            Assert.AreEqual("测试", restored.Name);
            Assert.AreEqual(7, restored.Value);
            Assert.Throws<SerializationException>(delegate { serializer.Deserialize<TestDto>(string.Empty); });
        }

        [Serializable]
        public sealed class TestDto
        {
            public string Name;
            public int Value;
        }

        private sealed class SilentLogger : Yato.Core.ILogger
        {
            public void Debug(string message)
            {
            }

            public void Info(string message)
            {
            }

            public void Warn(string message)
            {
            }

            public void Error(string message, Exception exception = null)
            {
            }
        }
    }
}
