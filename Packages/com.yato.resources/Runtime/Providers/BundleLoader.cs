using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>
    /// 单个 bundle 的加载器：单飞（同一 bundle 并发请求只发一次）、平台分流、
    /// 失败统一抛 <see cref="ResourceException"/>（RES-01/RES-04 的修复）。
    /// </summary>
    public sealed class BundleLoader : IDisposable
    {
        private readonly Dictionary<string, AssetBundle> m_loaded =
            new Dictionary<string, AssetBundle>(StringComparer.Ordinal);

        private readonly Dictionary<string, PendingBundle> m_pending =
            new Dictionary<string, PendingBundle>(StringComparer.Ordinal);

        private readonly BundleRefCounter m_refCounter;
        private readonly string m_snapshotDirectory;
        private readonly HashSet<UnityWebRequest> m_webRequests = new HashSet<UnityWebRequest>();
        private bool m_disposed;

        // Allows cancellation/lifecycle tests to drive the shared operation without an actual bundle file.
        internal Func<string, CancellationToken, IAwaitable> LoadOverride;

        public BundleLoader(BundleRefCounter refCounter, string snapshotDirectory = null)
        {
            if (refCounter == null)
            {
                throw new ArgumentNullException("refCounter");
            }

            m_refCounter = refCounter;
            m_snapshotDirectory = snapshotDirectory ?? (RuntimePaths.PreferPersistent && System.IO.File.Exists(RuntimePaths.GetManifestPath(true))
                ? System.IO.Path.GetDirectoryName(RuntimePaths.GetManifestPath(true)) : RuntimePaths.StreamingAssetsBundleDir);
            HotUpdateMaintenance.MarkUsed(m_snapshotDirectory);
        }

        /// <summary>已加载的 bundle（未加载返回 null）。</summary>
        public AssetBundle GetBundle(string bundleName)
        {
            AssetBundle bundle;
            return !string.IsNullOrEmpty(bundleName) && m_loaded.TryGetValue(bundleName, out bundle) ? bundle : null;
        }

        /// <summary>主线程加载 bundle（单飞）；每个等待者独立取消，全部取消才中止共享加载。</summary>
        public IAwaitable LoadAsync(string bundleName, CancellationToken ct)
        {
            var immediate = new AwaitableCompletionSource();

            if (m_disposed)
            {
                immediate.SetException(new ObjectDisposedException(nameof(BundleLoader)));
                return immediate;
            }
            if (ct.IsCancellationRequested)
            {
                immediate.SetException(new OperationCanceledException(ct));
                return immediate;
            }

            if (string.IsNullOrEmpty(bundleName))
            {
                immediate.SetException(new ResourceException(null, null, ResourceStage.BundleLoad, "bundle 名为空"));
                return immediate;
            }

            if (m_loaded.ContainsKey(bundleName))
            {
                immediate.SetResult();
                return immediate;
            }

            PendingBundle pending;
            if (m_pending.TryGetValue(bundleName, out pending))
            {
                var waiter = pending.AddWaiter(ct);
                return waiter ?? RetryAfterPending(bundleName, pending, ct);
            }

            pending = new PendingBundle();
            m_pending[bundleName] = pending;
            IAwaitable result = pending.AddWaiter(ct);
            IAwaitable loading;
            try
            {
                loading = LoadOverride != null ? LoadOverride(bundleName, pending.Token) : LoadInternal(bundleName, pending.Token);
                if (loading == null) throw new InvalidOperationException("Bundle loader returned no operation");
            }
            catch (Exception error)
            {
                m_pending.Remove(bundleName);
                pending.Complete(error);
                return result;
            }

            loading.Completed += delegate
            {
                PendingBundle registered;
                if (m_pending.TryGetValue(bundleName, out registered) && ReferenceEquals(registered, pending))
                    m_pending.Remove(bundleName);
                Exception error = null;
                try { loading.GetResult(); }
                catch (Exception failure) { error = failure; }
                pending.Complete(error);
            };

            return result;
        }

        private IAwaitable RetryAfterPending(string bundleName, PendingBundle pending, CancellationToken ct)
        {
            // Native file loads cannot be aborted. Drain the abandoned operation before starting
            // another load of the same file, otherwise Unity rejects duplicate bundle residency.
            var result = new AwaitableCompletionSource();
            var registration = ct.Register(() => result.SetException(new OperationCanceledException(ct)));
            result.Completed += () => registration.Dispose();
            pending.Finished.Completed += () =>
            {
                if (result.IsDone) return;
                var retry = LoadAsync(bundleName, ct);
                retry.Completed += () =>
                {
                    try { retry.GetResult(); result.SetResult(); }
                    catch (Exception error) { result.SetException(error); }
                };
            };
            return result;
        }

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            // A destroyed RuntimeHost may never advance the coroutine again. Abort native
            // requests here; the coroutine still owns Dispose and removes them in its finally.
            foreach (var request in new List<UnityWebRequest>(m_webRequests)) request.Abort();
            var pendings = new List<PendingBundle>(m_pending.Values);
            m_pending.Clear();
            foreach (var pending in pendings) pending.CancelAll();
            UnloadAll(false);
        }

        /// <summary>卸载 bundle：只允许 <c>Unload(false)</c>（RES-05）。</summary>
        public void Unload(string bundleName)
        {
            AssetBundle bundle;
            if (!m_loaded.TryGetValue(bundleName, out bundle))
            {
                return;
            }

            m_loaded.Remove(bundleName);

            if (bundle == null)
            {
                return;
            }

            try
            {
                bundle.Unload(false);
            }
            catch (Exception e)
            {
                Log.Error("BundleLoader", "卸载失败：" + bundleName, e);
            }
        }

        /// <summary>卸载全部（Shutdown 时才允许 Unload(true)）。</summary>
        public void UnloadAll(bool unloadAllLoadedObjects = false)
        {
            foreach (KeyValuePair<string, AssetBundle> pair in m_loaded)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                try
                {
                    pair.Value.Unload(unloadAllLoadedObjects);
                }
                catch (Exception e)
                {
                    Log.Error("BundleLoader", "卸载失败：" + pair.Key, e);
                }
            }

            m_loaded.Clear();
        }

        private IAwaitable LoadInternal(string bundleName, CancellationToken ct)
        {
            var source = new AwaitableCompletionSource();
            string path;

            try
            {
                path = RuntimePaths.ResolveBundlePath(bundleName, m_snapshotDirectory);
            }
            catch (Exception e)
            {
                source.SetException(new ResourceException(null, bundleName, ResourceStage.BundleLoad, "解析 bundle 路径失败", e));
                return source;
            }

            if (RuntimePaths.RequiresWebRequest || path.Contains("://") || path.StartsWith("jar:", StringComparison.OrdinalIgnoreCase))
            {
                RuntimeHost.RunCoroutine(LoadByWebRequest(bundleName, path, source, ct));
            }
            else
            {
                LoadFromFile(bundleName, path, source, ct);
            }

            return source;
        }

        private void LoadFromFile(string bundleName, string path, AwaitableCompletionSource source, CancellationToken ct)
        {
            AssetBundleCreateRequest request;
            try
            {
                request = AssetBundle.LoadFromFileAsync(path);
            }
            catch (Exception e)
            {
                source.SetException(new ResourceException(null, bundleName, ResourceStage.BundleLoad, "LoadFromFile 失败：" + path, e));
                return;
            }

            if (request == null)
            {
                source.SetException(new ResourceException(null, bundleName, ResourceStage.BundleLoad, "LoadFromFile 返回空：" + path));
                return;
            }

            request.completed += delegate
            {
                try
                {
                    AssetBundle bundle = request.assetBundle;
                    if (bundle == null)
                    {
                        throw new ResourceException(null, bundleName, ResourceStage.BundleLoad, "bundle 加载结果为空：" + path);
                    }

                    Register(bundleName, bundle, ct);
                    source.SetResult();
                }
                catch (Exception e)
                {
                    source.SetException(e);
                }
            };
        }

        private IEnumerator LoadByWebRequest(string bundleName, string path, AwaitableCompletionSource source, CancellationToken ct)
        {
            string url = RuntimePaths.ToLoadPath(path);
            using (UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(url))
            {
                m_webRequests.Add(request);
                try
                {
                    if (m_disposed || ct.IsCancellationRequested)
                    {
                        source.SetException(new OperationCanceledException(ct));
                        yield break;
                    }
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (m_disposed || ct.IsCancellationRequested)
                        {
                            request.Abort();
                            source.SetException(new OperationCanceledException());
                            yield break;
                        }

                        yield return null;
                    }

                    if (m_disposed || ct.IsCancellationRequested)
                    {
                        source.SetException(new OperationCanceledException(ct));
                        yield break;
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        source.SetException(new ResourceException(
                            null,
                            bundleName,
                            ResourceStage.BundleLoad,
                            string.Format("下载失败：{0}（{1}）", url, request.error)));
                        yield break;
                    }

                    try
                    {
                        AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(request);
                        if (bundle == null)
                        {
                            throw new ResourceException(null, bundleName, ResourceStage.BundleLoad, "下载结果为空：" + url);
                        }

                        Register(bundleName, bundle, ct);
                        source.SetResult();
                    }
                    catch (Exception e)
                    {
                        source.SetException(e);
                    }
                }
                finally { m_webRequests.Remove(request); }
            }
        }

        private void Register(string bundleName, AssetBundle bundle, CancellationToken ct)
        {
            if (m_disposed || ct.IsCancellationRequested)
            {
                bundle.Unload(false);
                throw new OperationCanceledException(ct);
            }
            m_loaded[bundleName] = bundle;
            m_refCounter.MarkLoaded(bundleName, 0, 0f);
            Log.Debug(string.Format("[BundleLoader] 已加载 {0}", bundleName));
        }

        private sealed class PendingBundle
        {
            private readonly object m_gate = new object();
            private readonly CancellationTokenSource m_cancellation = new CancellationTokenSource();
            private readonly List<Waiter> m_waiters = new List<Waiter>();
            private int m_active;
            private bool m_done;
            private bool m_abandoned;
            private Exception m_error;
            public readonly AwaitableCompletionSource Finished = new AwaitableCompletionSource();
            public CancellationToken Token => m_cancellation.Token;
            public bool IsAbandoned { get { lock (m_gate) return m_abandoned; } }

            public IAwaitable AddWaiter(CancellationToken ct)
            {
                var waiter = new Waiter(this);
                bool done;
                Exception error;
                lock (m_gate)
                {
                    if (m_abandoned) return null;
                    done = m_done;
                    error = m_error;
                    if (!done) { m_waiters.Add(waiter); m_active++; }
                }
                if (done) waiter.Complete(error);
                else waiter.Listen(ct);
                return waiter.Result;
            }

            private void OnCancelled()
            {
                bool cancel;
                lock (m_gate)
                {
                    m_active--;
                    cancel = m_active == 0 && !m_done && !m_abandoned;
                    if (cancel) m_abandoned = true;
                }
                if (cancel)
                    try { m_cancellation.Cancel(); }
                    catch (ObjectDisposedException) { }
                    catch (AggregateException error) { Log.Warn("[BundleLoader] Cancellation observer failed: " + error.Message); }
            }

            public void Complete(Exception error)
            {
                Waiter[] waiters;
                lock (m_gate)
                {
                    if (m_done) return;
                    m_done = true;
                    m_error = error;
                    waiters = m_waiters.ToArray();
                    m_waiters.Clear();
                }
                foreach (var waiter in waiters) waiter.Complete(error);
                m_cancellation.Dispose();
                Finished.SetResult();
            }

            public void CancelAll()
            {
                Waiter[] waiters;
                lock (m_gate) waiters = m_waiters.ToArray();
                foreach (var waiter in waiters) waiter.Cancel();
                // Dispose also terminates callers waiting to retry an abandoned native load.
                Finished.SetResult();
            }

            private sealed class Waiter
            {
                public readonly AwaitableCompletionSource Result = new AwaitableCompletionSource();
                private readonly PendingBundle m_owner;
                private CancellationTokenRegistration m_registration;
                private int m_finished;
                public Waiter(PendingBundle owner) { m_owner = owner; }
                public void Listen(CancellationToken ct)
                {
                    if (Volatile.Read(ref m_finished) != 0) return;
                    m_registration = ct.Register(Cancel);
                    if (Volatile.Read(ref m_finished) != 0) m_registration.Dispose();
                }
                public void Complete(Exception error)
                {
                    if (Interlocked.Exchange(ref m_finished, 1) != 0) return;
                    m_registration.Dispose();
                    if (error == null) Result.SetResult(); else Result.SetException(error);
                }
                public void Cancel()
                {
                    if (Interlocked.Exchange(ref m_finished, 1) != 0) return;
                    m_owner.OnCancelled();
                    Result.SetException(new OperationCanceledException());
                    m_registration.Dispose();
                }
            }
        }
    }
}
