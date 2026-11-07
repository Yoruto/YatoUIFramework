using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.Core;
using Yato.Resources;

namespace Yato.Tests.PlayMode
{
    public sealed class TaskBridgeTests
    {
        [UnityTest] public IEnumerator BundleFactory_HostShutdownCancelsPendingManifestInitialization()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                var operation = BundleResourceServiceFactory.CreateAsync(manifestPath: "http://127.0.0.1:" + port + "/manifest.json");
                Assert.IsFalse(operation.IsDone);
                UnityEngine.Object.DestroyImmediate(RuntimeHost.Instance.gameObject);
                Assert.IsTrue(operation.IsDone, "Shutdown must finish initialization without another coroutine tick");
                Assert.Catch<OperationCanceledException>(() => operation.GetResult());
            }
            finally { listener.Stop(); }
            var recreated = RuntimeHost.Instance;
            yield return null;
        }

        [UnityTest] public IEnumerator BundleFactory_WorkerCancellationFinishesProviderAndDispatchesOnMainThread()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                    int mainThread = Thread.CurrentThread.ManagedThreadId, callbackThread = -1;
                    var operation = BundleResourceServiceFactory.CreateProviderAsync(
                        manifestPath: "http://127.0.0.1:" + port + "/manifest.json", ct: cancellation.Token);
                    operation.Completed += () => callbackThread = Thread.CurrentThread.ManagedThreadId;
                    var worker = new Thread(cancellation.Cancel);
                    worker.Start(); Assert.IsTrue(worker.Join(5000));
                    Assert.IsTrue(operation.IsDone);
                    Assert.Catch<OperationCanceledException>(() => operation.GetResult());
                    for (var frame = 0; frame < 30 && callbackThread == -1; frame++) yield return null;
                    Assert.AreEqual(mainThread, callbackThread);
                }
                finally { listener.Stop(); }
            }
        }

        [UnityTest] public IEnumerator ResourceFactory_LoadsUrlManifestWithoutSystemIoFileReads()
        {
            string root = Path.Combine(Path.GetTempPath(), "yato-url-manifest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "manifest.json");
            File.WriteAllText(path, "{\"schema\":2,\"version\":\"test\",\"platform\":\"" +
                RuntimePaths.PlatformName + "\",\"bundles\":{},\"assets\":{}}");
            IResourceProvider provider = null;
            try
            {
                var loading = BundleResourceServiceFactory.CreateProviderAsync(
                    manifestPath: new Uri(path).AbsoluteUri);
                yield return loading.AsEnumerator();
                provider = loading.GetResult();
                Assert.IsInstanceOf<BundleProvider>(provider);
            }
            finally
            {
                (provider as IDisposable)?.Dispose();
                Directory.Delete(root, true);
            }
        }

        [UnityTest] public IEnumerator CompletedOperations_SupportMultipleTaskConsumersAndDirectAwait()
        {
            var source = new Yato.Resources.AwaitableCompletionSource<int>();
            var one = source.AsTask(); var two = source.AsTask();
            var direct = Read(source);
            Assert.IsFalse(one.IsCompleted);
            source.SetResult(42);
            while (!direct.IsCompleted) yield return null;
            Assert.AreEqual(42, one.GetAwaiter().GetResult());
            Assert.AreEqual(42, two.GetAwaiter().GetResult());
            Assert.AreEqual(42, direct.GetAwaiter().GetResult());
            source.SetResult(99);
            Assert.AreEqual(42, source.GetResult(), "Repeated completion must not mutate an already published value");
        }
        private static async Task<int> Read(IAwaitable<int> source) => await source;

        [UnityTest] public IEnumerator WorkerCompletion_DispatchesToMainThreadAndPreservesFault()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId, callbackThread = -1;
            var completion = new TaskCompletionSource<int>();
            var operation = completion.Task.AsAwaitable();
            operation.Completed += () => callbackThread = Thread.CurrentThread.ManagedThreadId;
            var worker = Task.Run(() => completion.SetResult(7));
            while (!operation.IsDone) yield return null;
            Assert.AreEqual(7, operation.GetResult());
            Assert.AreEqual(mainThread, callbackThread);
            var failure = Task.Run((Func<int>)(() => throw new InvalidOperationException("bridge test fault"))).AsAwaitable();
            while (!failure.IsDone) yield return null;
            Assert.Throws<InvalidOperationException>(() => failure.GetResult());
            Assert.IsTrue(worker.IsCompleted);
        }
        [UnityTest] public IEnumerator Cancellation_PreservesActualSourceCancellation()
        {
            var canceled = Task.FromCanceled<int>(new CancellationToken(true)).AsAwaitable();
            Assert.Catch<OperationCanceledException>(() => canceled.GetResult());
            var task = canceled.AsTask();
            Assert.IsTrue(task.IsCanceled);
            var pending = new Yato.Resources.AwaitableCompletionSource();
            var waiting = pending.AsTask();
            yield return null;
            Assert.IsFalse(waiting.IsCompleted);
            pending.SetException(new OperationCanceledException());
            Assert.IsTrue(waiting.IsCanceled);
        }
        [UnityTest] public IEnumerator HostDestruction_CancelsMainThreadAdapterButPreservesOriginalOperations()
        {
            var original = new TaskCompletionSource<int>();
            var fromTask = original.Task.AsAwaitable();
            int notifications = 0;
            fromTask.Completed += () => notifications++;
            var source = new Yato.Resources.AwaitableCompletionSource<int>();
            var toTask = source.AsTask();
            UnityEngine.Object.Destroy(RuntimeHost.Instance.gameObject);
            yield return null;
            Assert.IsFalse(toTask.IsCompleted);
            Assert.Throws<OperationCanceledException>(() => fromTask.GetResult());
            Assert.IsFalse(original.Task.IsCompleted);
            var newHost = RuntimeHost.Instance;
            original.SetResult(5); source.SetResult(6);
            yield return null; yield return null;
            Assert.AreEqual(1, notifications);
            Assert.Throws<OperationCanceledException>(() => fromTask.GetResult());
            Assert.AreEqual(6, toTask.GetAwaiter().GetResult());
            Assert.IsTrue(MainThreadDispatcher.IsMainThread);
        }
        [UnityTest] public IEnumerator ThrowingCompletionObserver_DoesNotBlockTaskBridge()
        {
            var source = new Yato.Resources.AwaitableCompletionSource<int>();
            source.Completed += () => throw new InvalidOperationException("observer test fault");
            var task = source.AsTask();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("完成回调异常"));
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("InvalidOperationException: observer test fault"));
            source.SetResult(11);
            Assert.AreEqual(11, task.GetAwaiter().GetResult());
            yield return null;
        }

        [UnityTest] public IEnumerator OriginalOperation_WorkerCompletesAfterHostDestruction_StillNotifiesTaskConsumer()
        {
            var source = new Yato.Resources.AwaitableCompletionSource<int>();
            var task = source.AsTask();
            var mainThread = Thread.CurrentThread.ManagedThreadId;
            var callbackThread = -1;
            source.Completed += () => callbackThread = Thread.CurrentThread.ManagedThreadId;
            UnityEngine.Object.Destroy(RuntimeHost.Instance.gameObject);
            yield return null;
            var worker = new Thread(() => source.SetResult(73));
            worker.Start(); Assert.IsTrue(worker.Join(5000));
            var newHost = RuntimeHost.Instance;
            for (var frame = 0; frame < 120 && !task.IsCompleted; frame++) yield return null;
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(73, task.GetAwaiter().GetResult());
            Assert.AreEqual(mainThread, callbackThread);
        }
    }
}
