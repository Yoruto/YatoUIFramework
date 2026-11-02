using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>Main-thread adapters; ownership and cancellation remain with the original operation.
    /// Host shutdown cancels Task-to-IAwaitable adapters; Task results remain owned by the original caller.</summary>
    public static class TaskBridge
    {
        public static Task AsTask(this IAwaitable operation) => ToTask(operation, () => { operation.GetResult(); return true; });
        public static Task<T> AsTask<T>(this IAwaitable<T> operation) => ToTask(operation, () => operation.GetResult());
        public static TaskAwaiter GetAwaiter(this IAwaitable operation) => operation.AsTask().GetAwaiter();
        public static TaskAwaiter<T> GetAwaiter<T>(this IAwaitable<T> operation) => operation.AsTask().GetAwaiter();
        public static IAwaitable AsAwaitable(this Task task) => FromTask(task, () => { task.GetAwaiter().GetResult(); return true; });
        public static IAwaitable<T> AsAwaitable<T>(this Task<T> task) => FromTask(task, () => task.GetAwaiter().GetResult());

        private static Task<T> ToTask<T>(IAwaitable operation, Func<T> read)
        {
            RequireMainThread();
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action completed = null;
            completed = () =>
            {
                operation.Completed -= completed;
                try { result.TrySetResult(read()); }
                catch (OperationCanceledException error) { result.TrySetCanceled(error.CancellationToken); }
                catch (Exception error) { result.TrySetException(error); }
            };
            operation.Completed += completed;
            return result.Task;
        }

        private static IAwaitable<T> FromTask<T>(Task task, Func<T> read)
        {
            RequireMainThread();
            if (task == null) throw new ArgumentNullException(nameof(task));
            var result = new AwaitableCompletionSource<T>();
            var lifetime = RuntimeHost.LifetimeToken;
            var registration = lifetime.Register(() => result.SetException(new OperationCanceledException(lifetime)));
            task.ContinueWith(completed =>
            {
                Exception failure = null; T value = default;
                try { value = read(); } // Observe a fault even when the host has already stopped.
                catch (Exception error) { failure = error; }
                if (lifetime.IsCancellationRequested) { registration.Dispose(); return; }
                MainThreadDispatcher.Send(() =>
                {
                    registration.Dispose();
                    if (lifetime.IsCancellationRequested) return;
                    if (failure == null) result.SetResult(value); else result.SetException(failure);
                });
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return result;
        }
        private static void RequireMainThread()
        {
            if (!MainThreadDispatcher.IsMainThread || !RuntimeHost.IsAlive)
                throw new InvalidOperationException("Create task adapters on the Unity main thread with a live RuntimeHost (Play Mode).");
        }
    }
}
