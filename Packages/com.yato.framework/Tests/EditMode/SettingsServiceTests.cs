using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yato.Core;
using Yato.Persistence;
using Yato.Settings;

namespace Yato.Tests.EditMode
{
    /// <summary>
    /// 设置服务回归（D05）：默认值模板隔离、加载顺序、缺文件/损坏/非法、
    /// 变化通知与退订、显式保存与校验。
    /// </summary>
    public sealed class SettingsServiceTests
    {
        private string m_root;
        private FileStore m_store;
        private JsonUtilitySerializer m_serializer;

        [SetUp]
        public void SetUp()
        {
            Log.SetLogger(new SilentLogger());

            m_root = Path.Combine(Path.GetTempPath(), "yato-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);

            m_store = new FileStore(m_root);
            m_serializer = new JsonUtilitySerializer();
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
        public void Constructor_KeepsDefaultsAsTemplate()
        {
            SettingsService<TestSettings> service = CreateService();

            service.Apply(delegate(TestSettings settings) { settings.Volume = 1; });

            Assert.AreEqual(80, service.CreateDefaultsSnapshot().Volume, "运行期数据不能改写默认模板");
            Assert.AreEqual(80, service.ResetToDefaults().Volume);
        }

        [Test]
        public void Current_ReturnsSnapshot_MutationDoesNotAffectService()
        {
            SettingsService<TestSettings> service = CreateService();
            service.Apply(delegate(TestSettings settings) { settings.Volume = 42; });

            int changes = 0;
            service.Changed += delegate { changes++; };

            service.Current.Volume = 5;

            Assert.AreEqual(42, service.Current.Volume, "Current 是快照，修改它不能改变服务状态");
            Assert.AreEqual(0, changes, "直接修改快照不能触发 Changed");
        }

        [Test]
        public void Load_MissingFileTwice_RestoresDefaultsInsteadOfKeepingAppliedValues()
        {
            SettingsService<TestSettings> service = CreateService();

            Assert.AreEqual(SettingsLoadStatus.DefaultsUsed, service.Load().Status);
            service.Apply(delegate(TestSettings settings) { settings.Volume = 30; });
            Assert.AreEqual(30, service.Current.Volume);

            // 磁盘文件仍然不存在：再次加载必须真正回到默认值
            SettingsLoadResult result = service.Load();

            Assert.AreEqual(SettingsLoadStatus.DefaultsUsed, result.Status);
            Assert.AreEqual(80, service.Current.Volume, "缺文件时必须恢复默认值");
            Assert.AreEqual("zh-Hans", service.Current.Language);
            Assert.IsFalse(m_store.Exists("settings/game.json"), "缺文件不应创建空文件");
        }

        [Test]
        public void LoadAsync_MissingFileTwice_RestoresDefaultsInsteadOfKeepingAppliedValues()
        {
            SettingsService<TestSettings> service = CreateService();

            Task.Run(delegate { return service.LoadAsync(); }).GetAwaiter().GetResult();
            service.Apply(delegate(TestSettings settings) { settings.Volume = 30; });

            SettingsLoadResult result = Task.Run(delegate { return service.LoadAsync(); })
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(SettingsLoadStatus.DefaultsUsed, result.Status);
            Assert.AreEqual(80, service.Current.Volume, "缺文件时必须恢复默认值（异步路径同样适用）");
        }

        [Test]
        public void Load_MissingFile_UsesDefaultsWithoutCreatingFile()
        {
            SettingsService<TestSettings> service = CreateService();
            int changes = 0;
            service.Changed += delegate { changes++; };

            SettingsLoadResult result = service.Load();

            Assert.AreEqual(SettingsLoadStatus.DefaultsUsed, result.Status);
            Assert.IsTrue(result.UsedDefaults);
            Assert.IsTrue(service.IsLoaded);
            Assert.AreEqual(80, service.Current.Volume);
            Assert.AreEqual(1, changes, "发布当前设置时必须通知");
            Assert.IsFalse(m_store.Exists("settings/game.json"), "缺文件不应创建空文件");
        }

        [Test]
        public void Load_UserFile_AppliesValues()
        {
            WriteSettings(new TestSettings { Volume = 35, Language = "en", Fullscreen = false });

            SettingsService<TestSettings> service = CreateService();
            SettingsLoadResult result = service.Load();

            Assert.AreEqual(SettingsLoadStatus.Loaded, result.Status);
            Assert.IsNull(result.Error);
            Assert.AreEqual(35, service.Current.Volume);
            Assert.AreEqual("en", service.Current.Language);
            Assert.IsFalse(service.Current.Fullscreen);
        }

        [Test]
        public void Load_CorruptedFile_KeepsFileAndUsesDefaults()
        {
            m_store.WriteAllText("settings/game.json", string.Empty);

            SettingsService<TestSettings> service = CreateService();
            SettingsLoadResult result = service.Load();

            Assert.AreEqual(SettingsLoadStatus.Corrupted, result.Status);
            Assert.IsNotNull(result.Error);
            Assert.AreEqual(80, service.Current.Volume);
            Assert.AreEqual(string.Empty, m_store.ReadAllText("settings/game.json"), "损坏文件必须原样保留");
        }

        [Test]
        public void Load_InvalidValues_UsesDefaultsAndKeepsFile()
        {
            WriteSettings(new TestSettings { Volume = -5, Language = "en", Fullscreen = true });

            SettingsService<TestSettings> service = CreateService(delegate(TestSettings settings)
            {
                if (settings.Volume < 0)
                {
                    throw new ArgumentOutOfRangeException("Volume");
                }
            });

            SettingsLoadResult result = service.Load();

            Assert.AreEqual(SettingsLoadStatus.Invalid, result.Status);
            Assert.AreEqual(80, service.Current.Volume);
            StringAssert.Contains("-5", m_store.ReadAllText("settings/game.json"), "非法文件不能被覆盖");
        }

        [Test]
        public void Apply_MutatesAndNotifiesWithoutWriting()
        {
            SettingsService<TestSettings> service = CreateService();
            int changes = 0;
            service.Changed += delegate { changes++; };

            service.Apply(delegate(TestSettings settings) { settings.Volume = 42; });

            Assert.AreEqual(42, service.Current.Volume);
            Assert.AreEqual(1, changes);
            Assert.IsFalse(m_store.Exists("settings/game.json"), "设置变化不自动落盘");
        }

        [Test]
        public void Save_WritesAndReloadsInNewService()
        {
            SettingsService<TestSettings> service = CreateService();
            service.Apply(delegate(TestSettings settings)
            {
                settings.Volume = 55;
                settings.Language = "ja";
            });
            service.Save();

            SettingsService<TestSettings> reloaded = CreateService();
            SettingsLoadResult result = reloaded.Load();

            Assert.AreEqual(SettingsLoadStatus.Loaded, result.Status);
            Assert.AreEqual(55, reloaded.Current.Volume);
            Assert.AreEqual("ja", reloaded.Current.Language);
        }

        [Test]
        public void Save_InvalidState_ThrowsAndKeepsDiskUnchanged()
        {
            SettingsService<TestSettings> service = CreateService(delegate(TestSettings settings)
            {
                if (settings.Volume < 0)
                {
                    throw new ArgumentOutOfRangeException("Volume");
                }
            });

            service.Apply(delegate(TestSettings settings) { settings.Volume = -1; });

            Assert.Throws<SettingsValidationException>(delegate { service.Save(); });
            Assert.IsFalse(m_store.Exists("settings/game.json"), "校验失败不能写盘");
        }

        [Test]
        public void ResetToDefaults_RestoresAndNotifies()
        {
            SettingsService<TestSettings> service = CreateService();
            service.Load();
            service.Apply(delegate(TestSettings settings) { settings.Volume = 5; });

            int changes = 0;
            service.Changed += delegate { changes++; };

            TestSettings restored = service.ResetToDefaults();

            Assert.AreEqual(80, restored.Volume);
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void Changed_Unsubscribe_StopsNotifications()
        {
            SettingsService<TestSettings> service = CreateService();
            int changes = 0;
            Action<TestSettings> handler = delegate { changes++; };

            service.Changed += handler;
            service.Apply(delegate(TestSettings settings) { settings.Volume = 1; });
            service.Changed -= handler;
            service.Apply(delegate(TestSettings settings) { settings.Volume = 2; });

            Assert.AreEqual(1, changes);
        }

        [Test]
        public void Changed_SubscriberException_DoesNotBlockOthers()
        {
            SettingsService<TestSettings> service = CreateService();
            int secondCalls = 0;

            service.Changed += delegate { throw new InvalidOperationException("boom"); };
            service.Changed += delegate { secondCalls++; };

            Assert.DoesNotThrow(delegate { service.Apply(delegate(TestSettings settings) { settings.Volume = 3; }); });
            Assert.AreEqual(1, secondCalls);
        }

        [Test]
        public void Load_Twice_IsStable()
        {
            SettingsService<TestSettings> service = CreateService();

            SettingsLoadResult first = service.Load();
            SettingsLoadResult second = service.Load();

            Assert.AreEqual(SettingsLoadStatus.DefaultsUsed, first.Status);
            Assert.AreEqual(SettingsLoadStatus.DefaultsUsed, second.Status);
            Assert.AreEqual(80, service.Current.Volume);
        }

        [Test]
        public void Async_SaveAndLoad_RoundTrip()
        {
            SettingsService<TestSettings> service = CreateService();
            service.Apply(delegate(TestSettings settings) { settings.Volume = 66; });

            // SettingsService 在调用方的同步上下文上发布事件；测试在独立任务中执行避免阻塞主线程
            Task.Run(delegate { return service.SaveAsync(); }).GetAwaiter().GetResult();

            SettingsService<TestSettings> reloaded = CreateService();
            SettingsLoadResult result = Task.Run(delegate { return reloaded.LoadAsync(); })
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(SettingsLoadStatus.Loaded, result.Status);
            Assert.AreEqual(66, reloaded.Current.Volume);
        }

        [Test]
        public void LoadAsync_PreCancelled_Throws()
        {
            WriteSettings(new TestSettings { Volume = 10, Language = "en", Fullscreen = true });

            SettingsService<TestSettings> service = CreateService();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(delegate
            {
                Task.Run(delegate { return service.LoadAsync(cts.Token); }).GetAwaiter().GetResult();
            });

            cts.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LoadAsync_LateReadCannotOverwriteRuntimeChange(bool reset)
        {
            WriteSettings(new TestSettings { Volume = 10 });
            var store = new HeldStore(m_store);
            var service = new SettingsService<TestSettings>(new TestSettings(), store, "settings/game.json", m_serializer);
            SettingsLoadResult initial = service.Load();
            int changes = 0;
            service.Changed += _ => changes++;

            Task<SettingsLoadResult> loading = service.LoadAsync();
            if (reset) service.ResetToDefaults();
            else service.Apply(settings => settings.Volume = 42);
            store.Read.SetResult(m_serializer.Serialize(new TestSettings { Volume = 15 }));

            SettingsLoadResult result = await loading;
            Assert.AreEqual(SettingsLoadStatus.Superseded, result.Status);
            Assert.IsFalse(result.UsedDefaults, "过期结果没有发布默认值");
            Assert.AreEqual(reset ? 80 : 42, service.Current.Volume);
            Assert.AreEqual(1, changes, "迟到读取不能再次发布设置");
            Assert.AreSame(initial, service.LastLoadResult);
        }

        [Test]
        public async Task LoadAsync_QueuedLoadsReadSeriallyAndOnlyNewestPublishes()
        {
            WriteSettings(new TestSettings { Volume = 30 });
            var store = new HeldStore(m_store);
            var service = new SettingsService<TestSettings>(new TestSettings(), store, "settings/game.json", m_serializer);
            int changes = 0;
            service.Changed += _ => changes++;

            Task<SettingsLoadResult> first = service.LoadAsync();
            Task<SettingsLoadResult> second = service.LoadAsync();
            Task<SettingsLoadResult> third = service.LoadAsync();
            Assert.AreEqual(1, store.ReadCalls, "旧读取释放前不得发起第二次文件读取");
            store.Read.SetResult(m_serializer.Serialize(new TestSettings { Volume = 10 }));

            Assert.AreEqual(SettingsLoadStatus.Superseded, (await first).Status);
            Assert.AreEqual(SettingsLoadStatus.Superseded, (await second).Status);
            SettingsLoadResult latest = await third;
            Assert.AreEqual(SettingsLoadStatus.Loaded, latest.Status);
            Assert.AreEqual(2, store.ReadCalls, "已经过期的排队请求不应再次读取文件");
            Assert.AreEqual(30, service.Current.Volume);
            Assert.AreEqual(1, changes);
            Assert.AreSame(latest, service.LastLoadResult);
        }

        [Test]
        public async Task Load_WhileAsyncReadPendingFailsWithoutBlockingOrInvalidatingIt()
        {
            WriteSettings(new TestSettings { Volume = 10 });
            var store = new HeldStore(m_store);
            var service = new SettingsService<TestSettings>(new TestSettings(), store, "settings/game.json", m_serializer);
            Task<SettingsLoadResult> loading = service.LoadAsync();

            Assert.Throws<FileBusyException>(() => service.Load());
            Assert.AreEqual(80, service.Current.Volume);
            Assert.IsFalse(service.IsLoaded);
            store.Read.SetResult(m_serializer.Serialize(new TestSettings { Volume = 10 }));
            Assert.AreEqual(SettingsLoadStatus.Loaded, (await loading).Status);
            Assert.AreEqual(10, service.Current.Volume);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Load_BusyFileDoesNotReplaceCurrentStateWithDefaults(bool asynchronous)
        {
            WriteSettings(new TestSettings { Volume = 10 });
            SettingsService<TestSettings> service = CreateService();
            SettingsLoadResult initial = service.Load();
            service.Apply(settings => settings.Volume = 42);
            int changes = 0;
            service.Changed += _ => changes++;

            using (m_store.AcquireLock("settings/game.json"))
            {
                if (asynchronous)
                {
                    Exception failure = null;
                    try { await service.LoadAsync(); }
                    catch (Exception error) { failure = error; }
                    Assert.IsInstanceOf<FileBusyException>(failure);
                }
                else Assert.Throws<FileBusyException>(() => service.Load());
            }

            Assert.AreEqual(42, service.Current.Volume);
            Assert.AreEqual(0, changes);
            Assert.AreSame(initial, service.LastLoadResult);
        }

        [Test]
        public async Task LoadAsync_CancellationAfterNonCooperativeReadDoesNotPublish()
        {
            WriteSettings(new TestSettings { Volume = 10 });
            var store = new HeldStore(m_store);
            var service = new SettingsService<TestSettings>(new TestSettings(), store, "settings/game.json", m_serializer);
            using (var cts = new CancellationTokenSource())
            {
                Task<SettingsLoadResult> loading = service.LoadAsync(cts.Token);
                cts.Cancel();
                store.Read.SetResult(m_serializer.Serialize(new TestSettings { Volume = 10 }));
                Exception failure = null;
                try { await loading; }
                catch (Exception error) { failure = error; }
                Assert.IsInstanceOf<OperationCanceledException>(failure);
                Assert.AreEqual(80, service.Current.Volume);
                Assert.IsFalse(service.IsLoaded);
                Assert.IsNull(service.LastLoadResult);
            }
        }

        [Test]
        public void Load_ValidatorReentryCannotOverwriteNewerApply()
        {
            WriteSettings(new TestSettings { Volume = 10 });
            SettingsService<TestSettings> service = null;
            service = CreateService(_ => service.Apply(settings => settings.Volume = 42));

            SettingsLoadResult result = service.Load();

            Assert.AreEqual(SettingsLoadStatus.Superseded, result.Status);
            Assert.AreEqual(42, service.Current.Volume);
            Assert.IsFalse(service.IsLoaded);
            Assert.IsNull(service.LastLoadResult);
        }

        [Test]
        public void Changed_ReentrantApplyDoesNotDeliverAnOlderSnapshotAfterNewerState()
        {
            SettingsService<TestSettings> service = CreateService();
            service.Changed += settings =>
            {
                if (settings.Volume == 10) service.Apply(value => value.Volume = 42);
            };
            var observed = new System.Collections.Generic.List<int>();
            service.Changed += settings => observed.Add(settings.Volume);

            service.Apply(settings => settings.Volume = 10);

            CollectionAssert.AreEqual(new[] { 42 }, observed);
            Assert.AreEqual(42, service.Current.Volume);
        }

        private sealed class HeldStore : IFileStore
        {
            private readonly IFileStore m_inner;
            public readonly TaskCompletionSource<string> Read = new TaskCompletionSource<string>();
            public int ReadCalls;
            public HeldStore(IFileStore inner) { m_inner = inner; }
            public string RootPath => m_inner.RootPath;
            public bool Exists(string path) => m_inner.Exists(path);
            public long GetFileSize(string path) => m_inner.GetFileSize(path);
            public DateTime GetLastWriteTimeUtc(string path) => m_inner.GetLastWriteTimeUtc(path);
            public byte[] ReadAllBytes(string path) => m_inner.ReadAllBytes(path);
            public string ReadAllText(string path) => m_inner.ReadAllText(path);
            public void WriteAllBytes(string path, byte[] data, CancellationToken ct = default) => m_inner.WriteAllBytes(path, data, ct);
            public void WriteAllText(string path, string text, CancellationToken ct = default) => m_inner.WriteAllText(path, text, ct);
            public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default) => m_inner.ReadAllBytesAsync(path, ct);
            public Task<string> ReadAllTextAsync(string path, CancellationToken ct = default) =>
                ++ReadCalls == 1 ? Read.Task : m_inner.ReadAllTextAsync(path, ct);
            public Task WriteAllBytesAsync(string path, byte[] data, CancellationToken ct = default) => m_inner.WriteAllBytesAsync(path, data, ct);
            public Task WriteAllTextAsync(string path, string text, CancellationToken ct = default) => m_inner.WriteAllTextAsync(path, text, ct);
            public bool Delete(string path) => m_inner.Delete(path);
            public void EnsureDirectory(string path) => m_inner.EnsureDirectory(path);
            public System.Collections.Generic.IReadOnlyList<string> EnumerateFiles(string path, string pattern, bool recursive = false) =>
                m_inner.EnumerateFiles(path, pattern, recursive);
            public IDisposable AcquireLock(string path) => m_inner.AcquireLock(path);
        }

        private void WriteSettings(TestSettings settings)
        {
            m_store.WriteAllText("settings/game.json", m_serializer.Serialize(settings));
        }

        private SettingsService<TestSettings> CreateService(Action<TestSettings> validator = null)
        {
            return new SettingsService<TestSettings>(
                new TestSettings { Volume = 80, Language = "zh-Hans", Fullscreen = true },
                m_store,
                "settings/game.json",
                m_serializer,
                validator);
        }

        [Serializable]
        public sealed class TestSettings
        {
            public int Volume = 80;
            public string Language = "zh-Hans";
            public bool Fullscreen = true;
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
