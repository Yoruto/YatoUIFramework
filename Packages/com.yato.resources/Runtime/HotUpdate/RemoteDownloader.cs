using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>Bounded downloads with unique temporary files and atomic destination replacement.</summary>
    public sealed class RemoteDownloader
    {
        public int MaxRetry = 3;
        public float RetryDelaySeconds = 1f;
        public int TimeoutSeconds = 60;
        public long MaxFileBytes = VersionManifest.MaxBundleBytes;
        /// <summary>Explicit local-test opt-ins. Production accepts HTTPS only; redirects are disabled.</summary>
        public bool AllowFileUrls;
        public bool AllowInsecureLocalhost;

        public string ValidateUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Invalid update URL: " + url);
            bool allowed = uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host);
            allowed |= AllowFileUrls && uri.IsFile && !uri.IsUnc;
            allowed |= AllowInsecureLocalhost && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
            if (!allowed) throw new ArgumentException("Update URL requires HTTPS (local testing must be explicitly enabled): " + url);
            return uri.AbsoluteUri;
        }

        public string FileUrl(string root, string relative)
        {
            BundleRelativePath.Validate(relative);
            return ValidateUrl(ValidateUrl(root).TrimEnd('/') + "/" + relative);
        }

        public IAwaitable<long> DownloadAsync(string url, string savePath, uint expectedCrc = 0u,
            IProgress<float> progress = null, CancellationToken ct = default(CancellationToken),
            long expectedSize = -1, long maxBytes = -1, bool verifyCrc = false, string expectedSha256 = null)
        {
            var source = new AwaitableCompletionSource<long>();
            try
            {
                url = ValidateUrl(url);
                savePath = Path.GetFullPath(savePath);
                BundleRelativePath.EnsureNoLinks(savePath);
                if (!string.IsNullOrEmpty(expectedSha256) && !VersionManifest.BundleHash.IsSha256(expectedSha256))
                    throw new ArgumentException("Expected SHA-256 must be 64 hexadecimal characters");
                long limit = maxBytes < 0 ? MaxFileBytes : Math.Min(maxBytes, MaxFileBytes);
                if (limit <= 0 || expectedSize < -1 || expectedSize > limit || TimeoutSeconds <= 0 ||
                    MaxRetry < 1 || MaxRetry > 10 || RetryDelaySeconds < 0 || RetryDelaySeconds > 60)
                    throw new ArgumentException("Invalid download limits");
                RuntimeHost.RunCoroutine(Guard(DownloadRoutine(url, savePath, expectedCrc, progress, ct,
                    expectedSize, limit, verifyCrc || expectedCrc != 0, expectedSha256, source), source));
            }
            catch (Exception e) { source.SetException(e); }
            return source;
        }

        private static IEnumerator Guard(IEnumerator routine, AwaitableCompletionSource<long> source)
        {
            try
            {
                while (true)
                {
                    bool more;
                    try { more = routine.MoveNext(); }
                    catch (Exception e) { source.SetException(e); yield break; }
                    if (!more) yield break;
                    yield return routine.Current;
                }
            }
            finally { (routine as IDisposable)?.Dispose(); }
        }

        private IEnumerator DownloadRoutine(string url, string savePath, uint crc, IProgress<float> progress,
            CancellationToken ct, long expectedSize, long limit, bool verifyCrc, string expectedSha256,
            AwaitableCompletionSource<long> source)
        {
            Exception last = null;
            for (int attempt = 0; attempt < MaxRetry; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                BundleRelativePath.EnsureNoLinks(savePath);
                string directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string temporary = savePath + "." + Guid.NewGuid().ToString("N") + ".part";
                try
                {
                    using (var request = UnityWebRequest.Get(url))
                    {
                        request.downloadHandler = new LimitedFileHandler(temporary, limit);
                        request.timeout = TimeoutSeconds;
                        request.redirectLimit = 0;
                        var operation = request.SendWebRequest();
                        while (!operation.isDone)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (request.downloadedBytes > (ulong)limit) throw new IOException("Download size limit exceeded");
                            progress?.Report(request.downloadProgress);
                            yield return null;
                        }
                        ct.ThrowIfCancellationRequested();
                        ((LimitedFileHandler)request.downloadHandler).Finish();
                        if (((LimitedFileHandler)request.downloadHandler).LimitExceeded)
                            throw new IOException("Download size limit exceeded: " + url);
                        if (request.result != UnityWebRequest.Result.Success)
                            last = new IOException("Download failed: " + url + " (" + request.error + ")");
                        else
                        {
                            long size = new FileInfo(temporary).Length;
                            if (size > limit || (expectedSize >= 0 && size != expectedSize))
                                throw new IOException("Downloaded length mismatch or size limit exceeded: " + url);
                            if (verifyCrc || !string.IsNullOrEmpty(expectedSha256))
                            {
                                uint actualCrc;
                                string actualSha256;
                                IntegrityUtil.Compute(temporary, !string.IsNullOrEmpty(expectedSha256),
                                    out actualCrc, out actualSha256);
                                if (verifyCrc && actualCrc != crc)
                                    throw new IOException("CRC verification failed: " + url);
                                if (!string.IsNullOrEmpty(expectedSha256) &&
                                    !string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                                    throw new IOException("SHA-256 verification failed: " + url);
                            }
                            ct.ThrowIfCancellationRequested();
                            BundleRelativePath.EnsureNoLinks(savePath);
                            if (File.Exists(savePath)) File.Replace(temporary, savePath, null);
                            else File.Move(temporary, savePath);
                            source.SetResult(size); // Cancellation after commit does not change this result.
                            yield break;
                        }
                    }
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                float until = Time.realtimeSinceStartup + RetryDelaySeconds;
                while (Time.realtimeSinceStartup < until)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return null;
                }
            }
            throw new ResourceException(null, url, ResourceStage.BundleDownload, "Download failed", last);
        }

        // Reject an oversized chunk before it reaches disk, including file:// responses.
        private sealed class LimitedFileHandler : DownloadHandlerScript
        {
            private readonly FileStream m_stream;
            private readonly long m_limit;
            public bool LimitExceeded { get; private set; }
            public LimitedFileHandler(string path, long limit) : base(new byte[64 * 1024])
            {
                m_limit = limit;
                m_stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (data == null || length < 0) return false;
                if (m_stream.Length + length > m_limit) { LimitExceeded = true; return false; }
                m_stream.Write(data, 0, length);
                return true;
            }
            private bool m_closed;
            public void Finish() { if (m_closed) return; m_stream.Flush(true); m_stream.Dispose(); m_closed = true; }
            protected override void CompleteContent() { Finish(); }
            public override void Dispose() { m_stream.Dispose(); base.Dispose(); }
        }
    }
}
