using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Yato.Core;

namespace Yato.Resources
{
    public sealed class HotUpdateResult
    {
        public bool Success;
        public bool Updated;
        public bool Canceled;
        public bool Busy;
        public int DownloadedBundles;
        public int SkippedBundles;
        public long DownloadedBytes;
        public string Version;
        public string Error;
        public override string ToString()
        {
            return string.Format("success={0} updated={1} downloaded={2} skipped={3} version={4} error={5}",
                Success, Updated, DownloadedBundles, SkippedBundles, Version, Error);
        }
    }

    /// <summary>Single-writer update transaction; failures retain the last committed snapshot.</summary>
    public sealed class HotUpdateService
    {
        private static int s_running;
        private readonly RemoteDownloader m_downloader;
        private readonly string m_storageRoot;
        private const long MetadataLimit = 4 * 1024 * 1024;

        public HotUpdateService(RemoteDownloader downloader = null, string storageRoot = null)
        {
            m_downloader = downloader ?? new RemoteDownloader();
            m_storageRoot = storageRoot ?? RuntimePaths.PersistentBundleDir;
        }

        public IAwaitable<HotUpdateResult> RunAsync(string remoteRootUrl, IProgress<float> progress = null,
            CancellationToken ct = default(CancellationToken))
        {
            var source = new AwaitableCompletionSource<HotUpdateResult>();
            if (Interlocked.CompareExchange(ref s_running, 1, 0) != 0)
            {
                source.SetResult(new HotUpdateResult { Busy = true, Error = "An update is already running" });
                return source;
            }
            try { RuntimeHost.RunCoroutine(Guard(remoteRootUrl, progress, ct, source)); }
            catch (Exception e)
            {
                Interlocked.Exchange(ref s_running, 0);
                source.SetResult(new HotUpdateResult { Error = e.ToString() });
            }
            return source;
        }

        private IEnumerator Guard(string url, IProgress<float> progress, CancellationToken ct,
            AwaitableCompletionSource<HotUpdateResult> source)
        {
            var result = new HotUpdateResult();
            HotUpdateStorage storage = null;
            string stage = null;
            IEnumerator routine = null;
            try
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    m_downloader.ValidateUrl(url);
                    storage = new HotUpdateStorage(m_storageRoot);
                    storage.CleanupStaging();
                    stage = storage.Begin();
                    routine = Update(url, storage, stage, progress, ct, result);
                }
                catch (Exception e) { Fail(result, e); }
                if (routine != null)
                    while (true)
                    {
                        bool more;
                        try { more = routine.MoveNext(); }
                        catch (Exception e) { Fail(result, e); break; }
                        if (!more) break;
                        yield return routine.Current;
                    }
            }
            finally
            {
                (routine as IDisposable)?.Dispose();
                try { storage?.Discard(stage); }
                catch (Exception e) { Log.Warn("[HotUpdate] Staging cleanup failed: " + e.Message); }
                Interlocked.Exchange(ref s_running, 0);
                source.SetResult(result);
            }
        }

        private static void Fail(HotUpdateResult result, Exception error)
        {
            result.Success = false;
            result.Canceled = error is OperationCanceledException;
            result.Error = error.ToString();
            Log.Warn("[HotUpdate] Last committed version retained: " + result.Error);
        }

        private IEnumerator Update(string root, HotUpdateStorage storage, string stage, IProgress<float> progress,
            CancellationToken ct, HotUpdateResult result)
        {
            string platform = RuntimePaths.PlatformName + "/";
            string versionFile = BundleRelativePath.Resolve(stage, RuntimePaths.VersionFileName);
            var download = m_downloader.DownloadAsync(m_downloader.FileUrl(root, platform + RuntimePaths.VersionFileName),
                versionFile, ct: ct, maxBytes: MetadataLimit);
            while (!download.IsDone) yield return null;
            result.DownloadedBytes += download.GetResult();
            var version = VersionManifest.LoadFromFile(versionFile);
            result.Version = version.AppVersion;
            string manifestFile = BundleRelativePath.Resolve(stage, RuntimePaths.ManifestFileName);
            download = m_downloader.DownloadAsync(m_downloader.FileUrl(root, platform + RuntimePaths.ManifestFileName),
                manifestFile, version.ManifestCrc, ct: ct, maxBytes: MetadataLimit, verifyCrc: true);
            while (!download.IsDone) yield return null;
            result.DownloadedBytes += download.GetResult();
            var manifest = ResourceManifest.LoadFromFile(manifestFile);
            ValidateManifests(version, manifest);

            string previous = storage.ActiveDirectory;
            bool sameMetadata = File.Exists(BundleRelativePath.Resolve(previous, RuntimePaths.ManifestFileName)) &&
                File.ReadAllText(BundleRelativePath.Resolve(previous, RuntimePaths.ManifestFileName)) == File.ReadAllText(manifestFile);
            long done = 0;
            foreach (KeyValuePair<string, VersionManifest.BundleHash> pair in version.Bundles)
            {
                ct.ThrowIfCancellationRequested();
                string destination = BundleRelativePath.Resolve(stage, pair.Key);
                string cached = BundleRelativePath.Resolve(previous, pair.Key);
                var hash = pair.Value;
                if (Verified(cached, hash))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(cached, destination);
                    result.SkippedBundles++;
                }
                else
                {
                    download = m_downloader.DownloadAsync(m_downloader.FileUrl(root, platform + pair.Key), destination,
                        hash.Crc, ct: ct, expectedSize: hash.Size, verifyCrc: true);
                    while (!download.IsDone) yield return null;
                    result.DownloadedBytes += download.GetResult();
                    result.DownloadedBundles++;
                }
                done++;
                progress?.Report((float)done / Math.Max(1, version.Bundles.Count));
            }
            // Revalidate the complete snapshot, including copied bundles, before switching its pointer.
            foreach (var pair in version.Bundles)
            {
                ct.ThrowIfCancellationRequested();
                if (!Verified(BundleRelativePath.Resolve(stage, pair.Key), pair.Value))
                    throw new IOException("Snapshot validation failed: " + pair.Key);
            }
            ct.ThrowIfCancellationRequested();
            bool changed = !sameMetadata || result.DownloadedBundles > 0 ||
                !File.Exists(BundleRelativePath.Resolve(previous, RuntimePaths.VersionFileName));
            if (changed) storage.Commit(stage); // Synchronous commit: no cancellation check after this point.
            result.Updated = changed;
            result.Success = true;
            try { progress?.Report(1f); }
            catch (Exception e) { Log.Warn("[HotUpdate] Progress observer failed after commit: " + e.Message); }
        }

        private static bool Verified(string path, VersionManifest.BundleHash hash)
        {
            BundleRelativePath.EnsureNoLinks(path);
            return File.Exists(path) && new FileInfo(path).Length == hash.Size && Crc32Util.Compute(path) == hash.Crc;
        }

        public static void ValidateManifests(VersionManifest version, ResourceManifest manifest)
        {
            if (version.Platform != manifest.Platform || version.AppVersion != manifest.Version ||
                version.Bundles.Count != manifest.BundleCount)
                throw new FormatException("version.json / manifest.json version, platform or bundle count mismatch");
            foreach (var pair in version.Bundles)
            {
                var bundle = manifest.GetBundle(pair.Key);
                if (bundle == null || bundle.Size != pair.Value.Size || bundle.Crc != pair.Value.Crc)
                    throw new FormatException("Manifest bundle mismatch: " + pair.Key);
                manifest.ResolveLoadOrder(pair.Key); // Reject missing dependencies and cycles.
            }
        }
    }
}
