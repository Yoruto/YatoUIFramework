using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Yato.Core;

namespace Yato.Resources
{
    public enum ResourceFailurePolicy
    {
        BundleRequired,
        AllowResourcesFallback
    }

    /// <summary>Explicit AssetBundle initialization in Editor and Player; Resources fallback must be requested.</summary>
    public static class BundleResourceServiceFactory
    {
        public static bool AutoCleanupPersistentCache = true;
        private static IBundleCachePolicy s_preparedPolicy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AutoCleanupPersistentCache = true;
            s_preparedPolicy = null;
        }

        public static IResourceService Create(ResourceFailurePolicy policy = ResourceFailurePolicy.BundleRequired, string manifestPath = null)
        {
            return ResourceServiceFactory.Create(CreateProvider(policy, manifestPath), true);
        }

        /// <summary>
        /// Cross-platform entry point for bundled resources. Android/WebGL StreamingAssets are
        /// URLs and must be read asynchronously; synchronous Create remains available when the
        /// manifest is already on the local filesystem or an explicit provider is supplied.
        /// </summary>
        public static IAwaitable<IResourceService> CreateAsync(
            ResourceFailurePolicy policy = ResourceFailurePolicy.BundleRequired, string manifestPath = null,
            CancellationToken ct = default)
        {
            var result = new AwaitableCompletionSource<IResourceService>();
            var loading = CreateProviderAsync(policy, manifestPath, ct);
            loading.Completed += () =>
            {
                IResourceProvider provider = null;
                try
                {
                    provider = loading.GetResult();
                    var service = ResourceServiceFactory.Create(provider, true);
                    provider = null;
                    result.SetResult(service);
                }
                catch (Exception error)
                {
                    (provider as IDisposable)?.Dispose();
                    result.SetException(error);
                }
            };
            return result;
        }

        public static IAwaitable<IResourceProvider> CreateProviderAsync(
            ResourceFailurePolicy policy = ResourceFailurePolicy.BundleRequired, string manifestPath = null,
            CancellationToken ct = default)
        {
            var result = new AwaitableCompletionSource<IResourceProvider>();
            try
            {
                ct.ThrowIfCancellationRequested();
                PreparePersistentCache();
                string path = manifestPath ?? ResolveManifestPath();
                if (IsRemoteManifestPath(path))
                {
                    var host = RuntimeHost.Instance;
                    var initialization = new ManifestInitialization(policy, result, ct, RuntimeHost.LifetimeToken);
                    try
                    {
                        if (!initialization.IsFinished) host.StartCoroutine(LoadManifestFromUrl(path, initialization));
                    }
                    catch (Exception error) { initialization.Fail(error); }
                }
                else
                {
                    ct.ThrowIfCancellationRequested();
                    result.SetResult(CreateBundleOrFallback(policy, path));
                }
            }
            catch (OperationCanceledException error) { result.SetException(error); }
            catch (Exception error) { CompleteProviderFailure(policy, error, result); }
            return result;
        }

        /// <summary>按当前平台/配置选择 Provider。</summary>
        public static IResourceProvider CreateProvider(ResourceFailurePolicy policy = ResourceFailurePolicy.BundleRequired, string manifestPath = null)
        {
            return CreateBundleOrFallback(policy, manifestPath);
        }

        private static IResourceProvider CreateBundleOrFallback(ResourceFailurePolicy policy, string manifestPath)
        {
            try
            {
                PreparePersistentCache();
                manifestPath = manifestPath ?? ResolveManifestPath();
                if (IsRemoteManifestPath(manifestPath))
                    throw new InvalidOperationException("StreamingAssets manifest is a URL; use BundleResourceServiceFactory.CreateAsync on Android/WebGL");
                ResourceManifest manifest = ResourceManifest.LoadFromFile(manifestPath);
                return CreateBundleProvider(manifest, manifestPath);
            }
            catch (Exception e)
            {
                var error = new ResourceException(null, null, ResourceStage.ManifestLoad,
                    string.Format("Provider=BundleProvider stage=ManifestLoad platform={0} manifest={1}: {2}",
                        RuntimePaths.PlatformName, manifestPath, e.Message), e);
                if (policy != ResourceFailurePolicy.AllowResourcesFallback) throw error;
                Log.Warn("[Yato.Resources] Explicit Resources fallback: " + error);
                return new ResourcesProvider();
            }
        }

        private static IResourceProvider CreateBundleProvider(ResourceManifest manifest, string manifestPath)
        {
            if (manifest.Platform != RuntimePaths.PlatformName)
                throw new FormatException("Manifest platform mismatch: " + manifest.Platform);
            foreach (var bundle in manifest.Bundles) manifest.ResolveLoadOrder(bundle.Key);

            BundleLoader loader = null;
            var refCounter = new BundleRefCounter(bundleName => loader?.Unload(bundleName));
            int lastSlash = manifestPath.LastIndexOf('/');
            string directory = IsRemoteManifestPath(manifestPath)
                ? manifestPath.Substring(0, lastSlash) : Path.GetDirectoryName(manifestPath);
            loader = new BundleLoader(refCounter, directory);
            Log.Info(string.Format("[Yato.Resources] 使用 BundleProvider：version={0} bundles={1} assets={2}",
                manifest.Version, manifest.BundleCount, manifest.AssetCount));
            return new BundleProvider(manifest, loader, refCounter);
        }

        internal static bool IsRemoteManifestPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith("jar:", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerator LoadManifestFromUrl(string path, ManifestInitialization initialization)
        {
            // This URL is a Unity StreamingAssets address, not an arbitrary hot-update endpoint.
            using (var request = UnityWebRequest.Get(path))
            {
                initialization.AttachRequest(request);
                try
                {
                    if (initialization.IsFinished) yield break;
                    request.timeout = 60;
                    request.redirectLimit = 0;
                    UnityWebRequestAsyncOperation operation = null;
                    Exception startupError = null;
                    try
                    {
                        operation = request.SendWebRequest();
                    }
                    catch (Exception error) { startupError = error; }
                    if (startupError != null)
                    {
                        initialization.Fail(startupError);
                        yield break;
                    }
                    while (!operation.isDone)
                    {
                        if (initialization.IsFinished) yield break;
                        if (request.downloadedBytes > 4 * 1024 * 1024)
                        {
                            request.Abort();
                            initialization.Fail(new IOException("StreamingAssets manifest exceeds 4 MiB"));
                            yield break;
                        }
                        yield return null;
                    }
                    if (initialization.IsFinished) yield break;
                    try
                    {
                        if (request.result != UnityWebRequest.Result.Success)
                            throw new IOException("StreamingAssets manifest request failed: " + request.error);
                        if (request.downloadedBytes > 4 * 1024 * 1024)
                            throw new IOException("StreamingAssets manifest exceeds 4 MiB");
                        initialization.Complete(CreateBundleProvider(ResourceManifest.Parse(request.downloadHandler.text, path), path));
                    }
                    catch (Exception error) { initialization.Fail(error); }
                }
                finally
                {
                    initialization.DetachRequest();
                    initialization.Abandon();
                }
            }
        }

        // Completion and cancellation can race while a provider is being constructed. Reserve
        // the single outcome before publishing it; a provider that loses the race stays ours to dispose.
        private sealed class ManifestInitialization
        {
            private readonly ResourceFailurePolicy m_policy;
            private readonly AwaitableCompletionSource<IResourceProvider> m_result;
            private readonly int m_ownerThread = Thread.CurrentThread.ManagedThreadId;
            private CancellationTokenRegistration m_callerCancellation;
            private CancellationTokenRegistration m_hostCancellation;
            private UnityWebRequest m_request;
            private int m_finished;

            public bool IsFinished { get { return Volatile.Read(ref m_finished) != 0; } }

            public ManifestInitialization(ResourceFailurePolicy policy, AwaitableCompletionSource<IResourceProvider> result,
                CancellationToken caller, CancellationToken host)
            {
                m_policy = policy;
                m_result = result;
                m_callerCancellation = caller.Register(() => Cancel(caller));
                m_hostCancellation = host.Register(() => Cancel(host));
                // Register synchronously invokes its observer for an already canceled token.
                if (IsFinished) DisposeRegistrations();
            }

            public void AttachRequest(UnityWebRequest request)
            {
                m_request = request;
                if (IsFinished) AbortOnMainThread();
            }

            public void DetachRequest() { m_request = null; }

            public void Complete(IResourceProvider provider)
            {
                if (Interlocked.Exchange(ref m_finished, 1) != 0)
                {
                    (provider as IDisposable)?.Dispose();
                    return;
                }
                DisposeRegistrations();
                m_result.SetResult(provider);
            }

            public void Fail(Exception error)
            {
                if (error is OperationCanceledException)
                {
                    FinishException(error);
                    return;
                }
                if (m_policy == ResourceFailurePolicy.AllowResourcesFallback)
                {
                    if (IsFinished) return;
                    Log.Warn("[Yato.Resources] Explicit Resources fallback: " + error);
                    Complete(new ResourcesProvider());
                }
                else FinishException(error);
            }

            public void Abandon() { Cancel(default(CancellationToken)); }

            private void Cancel(CancellationToken token)
            {
                if (Interlocked.Exchange(ref m_finished, 1) != 0) return;
                try
                {
                    DisposeRegistrations();
                    if (Thread.CurrentThread.ManagedThreadId == m_ownerThread) AbortOnMainThread();
                    else MainThreadDispatcher.PostCompletion(AbortOnMainThread);
                }
                finally { m_result.SetException(new OperationCanceledException(token)); }
            }

            private void FinishException(Exception error)
            {
                if (Interlocked.Exchange(ref m_finished, 1) != 0) return;
                DisposeRegistrations();
                m_result.SetException(error);
            }

            private void DisposeRegistrations()
            {
                m_callerCancellation.Dispose();
                m_hostCancellation.Dispose();
            }

            private void AbortOnMainThread() { m_request?.Abort(); }
        }

        private static void CompleteProviderFailure(ResourceFailurePolicy policy, Exception error,
            AwaitableCompletionSource<IResourceProvider> result)
        {
            if (policy == ResourceFailurePolicy.AllowResourcesFallback)
            {
                Log.Warn("[Yato.Resources] Explicit Resources fallback: " + error);
                result.SetResult(new ResourcesProvider());
            }
            else result.SetException(error);
        }

        // Cache behavior is supplied by an optional policy; the Bundle assembly never depends on HotUpdate.
        private static void PreparePersistentCache()
        {
            if (!AutoCleanupPersistentCache || !Application.isPlaying) return;
            var policy = BundleCachePolicy.Current;
            if (ReferenceEquals(s_preparedPolicy, policy)) return;
            s_preparedPolicy = policy;
            try { policy.PrepareStartup(); }
            catch (Exception error) { Log.Warn("[Yato.Resources] Startup bundle cache maintenance failed: " + error.Message); }
        }

        private static string ResolveManifestPath()
        {
            if (RuntimePaths.PreferPersistent)
            {
                string persistent = RuntimePaths.GetManifestPath(true);
                if (File.Exists(persistent))
                {
                    return persistent;
                }
            }

            return RuntimePaths.GetManifestPath(false);
        }
    }
}
