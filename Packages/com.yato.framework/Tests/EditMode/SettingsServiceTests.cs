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
