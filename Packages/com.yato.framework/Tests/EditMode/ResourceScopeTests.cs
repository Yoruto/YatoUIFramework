using System;
using System.Collections.Generic;
using NUnit.Framework;
using Yato.App;
using Yato.Core;

namespace Yato.Tests.EditMode
{
    /// <summary>
    /// 场景/全局资源所有权范围（D03）的纯逻辑回归：
    /// 显式登记、反向释放、范围结束后的迟到结果立即回收。
    /// </summary>
    public sealed class ResourceScopeTests
    {
        [SetUp]
        public void SetUp()
        {
            // 释放失败路径会记录错误日志，测试内静默避免误判
            Log.SetLogger(new SilentLogger());
        }

        [TearDown]
        public void TearDown()
        {
            Log.SetLogger(null);
        }

        [Test]
        public void Track_ReturnsSameInstanceAndCountsOnce()
        {
            var scope = new ResourceScope("scene");
            var handle = new FakeDisposable("a");

            FakeDisposable tracked = scope.Track(handle);

            Assert.AreSame(handle, tracked);
            Assert.AreEqual(1, scope.Count);
            Assert.IsFalse(scope.IsReleased);
            Assert.IsFalse(handle.Disposed);
        }

        [Test]
        public void ReleaseAll_DisposesInReverseOrder()
        {
            var scope = new ResourceScope("scene");
            var order = new List<string>();
            var first = new FakeDisposable("first", order);
            var second = new FakeDisposable("second", order);

            scope.Track(first);
            scope.Track(second);
            scope.ReleaseAll();

            CollectionAssert.AreEqual(new[] { "second", "first" }, order);
            Assert.AreEqual(0, scope.Count);
            Assert.IsTrue(scope.IsReleased);
        }

        [Test]
        public void Track_AfterRelease_DisposesImmediately()
        {
            var scope = new ResourceScope("scene");
            scope.ReleaseAll();

            var late = new FakeDisposable("late");
            FakeDisposable tracked = scope.Track(late);

            Assert.AreSame(late, tracked);
            Assert.IsTrue(late.Disposed, "范围结束后迟到的加载结果必须立即释放");
            Assert.AreEqual(0, scope.Count);
        }

        [Test]
        public void Release_Single_RemovesAndDisposesOnlyThatHandle()
        {
            var scope = new ResourceScope("scene");
            var first = new FakeDisposable("first");
            var second = new FakeDisposable("second");

            scope.Track(first);
            scope.Track(second);

            Assert.IsTrue(scope.Release(first));
            Assert.IsTrue(first.Disposed);
            Assert.IsFalse(second.Disposed);
            Assert.AreEqual(1, scope.Count);

            Assert.IsFalse(scope.Release(first), "重复释放应返回 false");
        }

        [Test]
        public void ReleaseAll_IsIdempotent()
        {
            var scope = new ResourceScope("scene");
            var handle = new FakeDisposable("a");
            scope.Track(handle);

            scope.ReleaseAll();
            scope.ReleaseAll();

            Assert.AreEqual(1, handle.DisposeCalls, "重复释放不能重复 Dispose 句柄");
        }

        [Test]
        public void ReleaseAll_ContinuesWhenOneDisposeThrows()
        {
            var scope = new ResourceScope("scene");
            var throwing = new ThrowingDisposable();
            var normal = new FakeDisposable("normal");

            scope.Track(throwing);
            scope.Track(normal);

            Assert.DoesNotThrow(delegate { scope.ReleaseAll(); });
            Assert.IsTrue(normal.Disposed, "单个句柄释放失败不能阻断其余资源");
        }

        private sealed class FakeDisposable : IDisposable
        {
            private readonly string m_name;
            private readonly List<string> m_order;

            public FakeDisposable(string name, List<string> order = null)
            {
                m_name = name;
                m_order = order;
            }

            public bool Disposed;
            public int DisposeCalls;

            public void Dispose()
            {
                Disposed = true;
                DisposeCalls++;

                if (m_order != null)
                {
                    m_order.Add(m_name);
                }
            }
        }

        private sealed class ThrowingDisposable : IDisposable
        {
            public void Dispose()
            {
                throw new InvalidOperationException("fake dispose failure");
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
