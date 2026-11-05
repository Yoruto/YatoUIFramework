using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Yato.Core;
using Yato.Resources;

namespace Yato.Tests.EditMode
{
    public sealed class ResourceAsyncLifecycleTests
    {
        private static void Pump() => typeof(MainThreadDispatcher).GetMethod("Pump", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { 0f });

        [Test]
        public void Completion_WorkerCompletesAndLateSubscribes_AllObserversRunOnMainThreadOnce()
        {
            var source = new AwaitableCompletionSource<int>();
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var observed = new List<int>();
            source.Completed += () => observed.Add(Thread.CurrentThread.ManagedThreadId);
            var worker = new Thread(() =>
            {
                source.SetResult(42);
                source.SetResult(99);
                source.Completed += () => observed.Add(Thread.CurrentThread.ManagedThreadId);
            });
            worker.Start();
            Assert.IsTrue(worker.Join(5000));
            Assert.IsTrue(source.IsDone);
            Assert.AreEqual(42, source.GetResult());
            Assert.AreEqual(0, observed.Count);
            Pump(); Pump();
            CollectionAssert.AreEqual(new[] { mainThread, mainThread }, observed);
        }

        [Test]
        public void Completion_SubscriptionRacesCompletion_NoObserverIsLostOrDuplicated()
        {
            for (var iteration = 0; iteration < 50; iteration++)
            {
                var source = new AwaitableCompletionSource<int>();
                var calls = 0;
                using (var ready = new ManualResetEventSlim())
                {
                    var worker = new Thread(() => { ready.Wait(); source.Completed += () => calls++; });
                    worker.Start();
                    ready.Set();
                    source.SetResult(iteration);
                    Assert.IsTrue(worker.Join(5000));
                    Pump();
                    Assert.AreEqual(1, calls);
                }
            }
        }

        [Test]
        public void Completion_ClearAndShutdown_PreserveTerminalNotification()
        {
            var source = new AwaitableCompletionSource<int>();
            var calls = 0;
            var ordinaryCalls = 0;
            source.Completed += () => calls++;
            var worker = new Thread(() => source.SetResult(1));
            worker.Start(); Assert.IsTrue(worker.Join(5000));
            MainThreadDispatcher.Post(() => ordinaryCalls++);
            MainThreadDispatcher.Clear();
            typeof(MainThreadDispatcher).GetMethod("Shutdown", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Assert.AreEqual(1, calls);
            Pump();
            Assert.AreEqual(1, calls);
            Assert.AreEqual(0, ordinaryCalls);
        }

        [Test]
        public void BundleLoad_OneWaiterCancels_OtherWaiterKeepsSharedOperation()
        {
            using (var firstCancellation = new CancellationTokenSource())
            using (var loader = new BundleLoader(new BundleRefCounter(_ => { })))
            {
                var operation = new AwaitableCompletionSource();
                var starts = 0;
                var sharedToken = default(CancellationToken);
                loader.LoadOverride = (name, ct) => { starts++; sharedToken = ct; return operation; };
                var first = loader.LoadAsync("ui/shared", firstCancellation.Token);
                var second = loader.LoadAsync("ui/shared", default(CancellationToken));
                firstCancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => first.GetResult());
                Assert.IsFalse(sharedToken.IsCancellationRequested);
                operation.SetResult();
                second.GetResult();
                Assert.AreEqual(1, starts);
            }
        }

        [Test]
        public void BundleLoad_AllWaitersCancel_RetryDrainsOldOperationBeforeRestarting()
        {
            using (var cancellation = new CancellationTokenSource())
            using (var loader = new BundleLoader(new BundleRefCounter(_ => { })))
            {
                var operations = new List<AwaitableCompletionSource>();
                var tokens = new List<CancellationToken>();
                loader.LoadOverride = (name, ct) =>
                {
                    var operation = new AwaitableCompletionSource(); operations.Add(operation); tokens.Add(ct); return operation;
                };
                var first = loader.LoadAsync("ui/shared", cancellation.Token);
                cancellation.Cancel();
                Assert.IsTrue(tokens[0].IsCancellationRequested);
                var retry = loader.LoadAsync("ui/shared", default(CancellationToken));
                Assert.AreEqual(1, operations.Count);
                operations[0].SetException(new OperationCanceledException());
                Assert.AreEqual(2, operations.Count);
                Assert.IsFalse(tokens[1].IsCancellationRequested);
                Assert.IsFalse(retry.IsDone);
                operations[1].SetResult();
                retry.GetResult();
                Assert.Throws<OperationCanceledException>(() => first.GetResult());
            }
        }

        [Test]
        public void BundleLoad_DisposeCancelsWaiters_AndRejectsNewLoadsAfterLateCompletion()
        {
            var loader = new BundleLoader(new BundleRefCounter(_ => { }));
            var operation = new AwaitableCompletionSource();
            loader.LoadOverride = (name, ct) => operation;
            var waiting = loader.LoadAsync("ui/shared", default(CancellationToken));
            loader.Dispose(); loader.Dispose();
            Assert.Throws<OperationCanceledException>(() => waiting.GetResult());
            operation.SetResult();
            Assert.Throws<ObjectDisposedException>(() => loader.LoadAsync("ui/shared", default(CancellationToken)).GetResult());
            Assert.IsNull(loader.GetBundle("ui/shared"));
        }

        [Test]
        public void BundleLoad_DisposeTerminatesRetry_EvenWhenOldOperationNeverCompletes()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var loader = new BundleLoader(new BundleRefCounter(_ => { }));
                loader.LoadOverride = (name, ct) => new AwaitableCompletionSource();
                loader.LoadAsync("ui/shared", cancellation.Token);
                cancellation.Cancel();
                var retry = loader.LoadAsync("ui/shared", default(CancellationToken));
                loader.Dispose();
                Assert.IsTrue(retry.IsDone);
                Assert.Throws<ObjectDisposedException>(() => retry.GetResult());
            }
        }
    }
}
