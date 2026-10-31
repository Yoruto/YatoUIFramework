using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using Yato.App;
using Yato.Core;

namespace Yato.Tests.PlayMode
{
    /// <summary>
    /// GameBootstrap 集成测试（D02）：幂等初始化、按序初始化/反向关闭、
    /// 失败回滚、服务注册去重、DDOL 根与全局资源释放。
    /// </summary>
    public sealed class GameBootstrapTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Log.SetLogger(new SilentLogger());
            GameBootstrap.ResetForTests();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameBootstrap.ResetForTests();
            yield return null;
            Log.SetLogger(null);
        }

        [UnityTest]
        public IEnumerator Initialize_OrdersServicesAndShutsDownInReverse()
        {
            var log = new List<string>();

            // 注册按具体类型去重，所以两个服务必须是不同类型
            GameBootstrap.RegisterService(new SecondService(log));
            GameBootstrap.RegisterService(new FirstService(log));

            GameBootstrap.Initialize();

            Assert.IsTrue(GameBootstrap.IsInitialized);
            CollectionAssert.AreEqual(new[] { "init:First", "init:Second" }, log, "服务必须按 Order 升序初始化");

            GameBootstrap.Shutdown();

            CollectionAssert.AreEqual(
                new[] { "init:First", "init:Second", "shutdown:Second", "shutdown:First" },
                log,
                "关闭必须按初始化反序释放");
            Assert.IsFalse(GameBootstrap.IsInitialized);

            yield return null;
        }

        [UnityTest]
        public IEnumerator Initialize_IsIdempotent()
        {
            var log = new List<string>();
            GameBootstrap.RegisterService(new RecordingService("Only", 0, log));

            GameBootstrap.Initialize();
            GameBootstrap.Initialize();

            Assert.AreEqual(1, Count(log, "init:Only"), "重复初始化不能再次执行服务");
            Assert.AreEqual(2, GameBootstrap.StartedServices.Count, "默认 SceneService + 业务服务各一次");

            yield return null;
        }

        [UnityTest]
        public IEnumerator Initialize_Failure_RollsBackStartedServices()
        {
            var log = new List<string>();
            GameBootstrap.RegisterService(new RecordingService("Good", 10, log));
            GameBootstrap.RegisterService(new FailingService("Bad", 20, log));

            Assert.Throws<AppInitializationException>(delegate { GameBootstrap.Initialize(); });

            Assert.IsFalse(GameBootstrap.IsInitialized);
            Assert.AreEqual(0, GameBootstrap.StartedServices.Count);
            Assert.IsNotNull(GameBootstrap.LastInitializationError);
            CollectionAssert.AreEqual(
                new[] { "init:Good", "init:Bad", "shutdown:Bad", "shutdown:Good" },
                log,
                "失败服务尽力清理，已完成的服务必须回滚");

            yield return null;
        }

        [UnityTest]
        public IEnumerator RegisterService_DuplicateType_IsIgnored()
        {
            var log = new List<string>();
            GameBootstrap.RegisterService(new RecordingService("Dup", 0, log));
            GameBootstrap.RegisterService(new RecordingService("Dup2", 0, log));

            GameBootstrap.Initialize();

            Assert.AreEqual(1, Count(log, "init:Dup"), "同类型重复注册只保留第一个");
            Assert.AreEqual(0, Count(log, "init:Dup2"));

            yield return null;
        }

        [UnityTest]
        public IEnumerator Initialize_CreatesDontDestroyOnLoadRootAndRegistersSceneService()
        {
            GameBootstrap.Initialize();

            Assert.IsNotNull(GameBootstrap.Instance);
            Assert.AreEqual("DontDestroyOnLoad", GameBootstrap.Instance.gameObject.scene.name);

            ISceneService sceneService = GameBootstrap.Scene;
            Assert.IsNotNull(sceneService);

            ISceneService resolved;
            Assert.IsTrue(ServiceLocator.Default.TryResolve<ISceneService>(out resolved));
            Assert.AreSame(sceneService, resolved);

            yield return null;
        }

        [UnityTest]
        public IEnumerator Shutdown_ReleasesGlobalResources()
        {
            GameBootstrap.Initialize();

            ResourceScope scope = GameBootstrap.GlobalResources;
            Assert.IsNotNull(scope);

            var handle = new FakeDisposable();
            scope.Track(handle);

            GameBootstrap.Shutdown();

            Assert.IsTrue(handle.Disposed, "全局资源必须随应用关闭释放");
            Assert.IsNull(GameBootstrap.GlobalResources);

            yield return null;
        }

        private static int Count(List<string> log, string value)
        {
            int count = 0;
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i] == value)
                {
                    count++;
                }
            }

            return count;
        }

        private class RecordingService : IAppService
        {
            private readonly string m_name;
            private readonly int m_order;
            private readonly List<string> m_log;

            public RecordingService(string name, int order, List<string> log)
            {
                m_name = name;
                m_order = order;
                m_log = log;
            }

            public string Name
            {
                get { return m_name; }
            }

            public int Order
            {
                get { return m_order; }
            }

            public void Initialize()
            {
                m_log.Add("init:" + m_name);
            }

            public void Shutdown()
            {
                m_log.Add("shutdown:" + m_name);
            }
        }

        /// <summary>Order 较大的服务；与 <see cref="FirstService"/> 类型不同，避免被注册去重。</summary>
        private sealed class SecondService : RecordingService
        {
            public SecondService(List<string> log)
                : base("Second", 20, log)
            {
            }
        }

        /// <summary>Order 较小的服务。</summary>
        private sealed class FirstService : RecordingService
        {
            public FirstService(List<string> log)
                : base("First", 10, log)
            {
            }
        }

        private sealed class FailingService : IAppService
        {
            private readonly string m_name;
            private readonly int m_order;
            private readonly List<string> m_log;

            public FailingService(string name, int order, List<string> log)
            {
                m_name = name;
                m_order = order;
                m_log = log;
            }

            public string Name
            {
                get { return m_name; }
            }

            public int Order
            {
                get { return m_order; }
            }

            public void Initialize()
            {
                m_log.Add("init:" + m_name);
                throw new InvalidOperationException("fake init failure");
            }

            public void Shutdown()
            {
                m_log.Add("shutdown:" + m_name);
            }
        }

        private sealed class FakeDisposable : IDisposable
        {
            public bool Disposed;

            public void Dispose()
            {
                Disposed = true;
            }
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
