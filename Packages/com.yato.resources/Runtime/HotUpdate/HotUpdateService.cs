using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>热更结果。</summary>
    public sealed class HotUpdateResult
    {
        public bool Success;
        public bool Updated;
        public int DownloadedBundles;
        public int SkippedBundles;
        public long DownloadedBytes;
        public string Version;
        public string Error;

        public override string ToString()
        {
            return string.Format(
                "success={0} updated={1} downloaded={2} skipped={3} version={4} error={5}",
                Success, Updated, DownloadedBundles, SkippedBundles, Version, Error);
        }
    }

    /// <summary>
    /// 热更流程（T-P5-01~04）：拉 version.json → 与本地 manifest 比对 → 下载差量 →
    /// CRC 校验 → 原子替换 manifest → 清理旧包；任一步失败则回退内置版本。
    /// </summary>
    public sealed class HotUpdateService
    {
        private readonly RemoteDownloader m_downloader;

        public HotUpdateService(RemoteDownloader downloader = null)
        {
            m_downloader = downloader ?? new RemoteDownloader();
        }

        public IAwaitable<HotUpdateResult> RunAsync(
            string remoteRootUrl,
            IProgress<float> progress = null,
            CancellationToken ct = default(CancellationToken))
        {
            var source = new AwaitableCompletionSource<HotUpdateResult>();
            RuntimeHost.RunCoroutine(RunRoutine(remoteRootUrl, progress, ct, source));
            return source;
        }

        private IEnumerator RunRoutine(
            string remoteRootUrl,
            IProgress<float> progress,
            CancellationToken ct,
            AwaitableCompletionSource<HotUpdateResult> source)
        {
            var result = new HotUpdateResult();

            if (string.IsNullOrEmpty(remoteRootUrl))
            {
                result.Error = "未配置远端地址";
                RollbackToBuiltin(result);
                source.SetResult(result);
                yield break;
            }

            string root = remoteRootUrl.TrimEnd('/');
            string platformDir = RuntimePaths.PersistentBundleDir;
            RuntimePaths.EnsurePersistentDir();

            // 1. version.json
            string remoteVersionUrl = string.Format("{0}/{1}/{2}", root, RuntimePaths.PlatformName, RuntimePaths.VersionFileName);
            string localVersionPath = Path.Combine(platformDir, RuntimePaths.VersionFileName);
            yield return Await(m_downloader.DownloadAsync(remoteVersionUrl, localVersionPath, 0u, null, ct), ct, result);
            if (!result.Success)
            {
                RollbackToBuiltin(result);
                source.SetResult(result);
                yield break;
            }

            VersionManifest remoteVersion = null;
            try
            {
                remoteVersion = VersionManifest.LoadFromFile(localVersionPath);
                result.Version = remoteVersion.AppVersion;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
                RollbackToBuiltin(result);
                source.SetResult(result);
                yield break;
            }

            // 2. 差量清单（按 CRC 比对本地已下载文件）
            var pending = new List<string>();
            long totalBytes = 0;
            foreach (KeyValuePair<string, VersionManifest.BundleHash> pair in remoteVersion.Bundles)
            {
                string localPath = Path.Combine(platformDir, pair.Key);
                bool upToDate = File.Exists(localPath)
                                && new FileInfo(localPath).Length == pair.Value.Size
                                && pair.Value.Crc != 0u
                                && Crc32Util.Compute(localPath) == pair.Value.Crc;
                if (upToDate)
                {
                    result.SkippedBundles++;
                    continue;
                }

                pending.Add(pair.Key);
                totalBytes += pair.Value.Size;
            }

            // 3. 逐个下载（文件级断点：已校验通过的直接跳过）
            long completedBytes = 0;
            for (int i = 0; i < pending.Count; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    result.Error = "已取消";
                    RollbackToBuiltin(result);
                    source.SetResult(result);
                    yield break;
                }

                string bundleName = pending[i];
                VersionManifest.BundleHash hash = remoteVersion.Bundles[bundleName];
                string url = string.Format("{0}/{1}/{2}", root, RuntimePaths.PlatformName, bundleName);
                string savePath = Path.Combine(platformDir, bundleName);

                yield return Await(m_downloader.DownloadAsync(url, savePath, hash.Crc, null, ct), ct, result);
                if (!result.Success)
                {
                    RollbackToBuiltin(result);
                    source.SetResult(result);
                    yield break;
                }

                result.DownloadedBundles++;
                completedBytes += hash.Size;
                if (progress != null)
                {
                    progress.Report(totalBytes > 0 ? (float)completedBytes / totalBytes : 1f);
                }
            }

            // 4. manifest.json（最后替换，保证失败时仍是旧版本可用）
            string remoteManifestUrl = string.Format("{0}/{1}/{2}", root, RuntimePaths.PlatformName, RuntimePaths.ManifestFileName);
            string localManifestPath = Path.Combine(platformDir, RuntimePaths.ManifestFileName);
            yield return Await(m_downloader.DownloadAsync(remoteManifestUrl, localManifestPath, remoteVersion.ManifestCrc, null, ct), ct, result);
            if (!result.Success)
            {
                RollbackToBuiltin(result);
                source.SetResult(result);
                yield break;
            }

            // 5. 清理旧包
            CleanupObsoleteBundles(platformDir, remoteVersion);

            result.Success = true;
            result.Updated = result.DownloadedBundles > 0;
            if (progress != null)
            {
                progress.Report(1f);
            }

            Log.Info("[HotUpdate] " + result);
            source.SetResult(result);
        }

        private static IEnumerator Await(IAwaitable<long> awaitable, CancellationToken ct, HotUpdateResult result)
        {
            while (!awaitable.IsDone)
            {
                yield return null;
            }

            try
            {
                result.DownloadedBytes += awaitable.GetResult();
            }
            catch (OperationCanceledException)
            {
                result.Error = "已取消";
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }

            if (string.IsNullOrEmpty(result.Error))
            {
                result.Success = true;
            }
            else
            {
                // result 跨步骤复用：前面步骤可能置过 true，失败必须显式清掉，
                // 否则 RunRoutine 的失败检查失效（CRC 损坏会被静默吞掉且不回退）
                result.Success = false;
            }
        }

        /// <summary>失败回退：删除持久化 manifest，运行时自动回到 StreamingAssets 内置版本。</summary>
        private static void RollbackToBuiltin(HotUpdateResult result)
        {
            result.Success = false;
            try
            {
                string manifestPath = RuntimePaths.GetManifestPath(true);
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }
            }
            catch (Exception e)
            {
                Log.Warn("[HotUpdate] 回退清理失败：" + e.Message);
            }

            Log.Warn("[HotUpdate] 失败，已回退内置版本：" + result.Error);
        }

        private static void CleanupObsoleteBundles(string platformDir, VersionManifest version)
        {
            try
            {
                string[] files = Directory.GetFiles(platformDir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    string relative = files[i].Substring(platformDir.Length).TrimStart('\\', '/').Replace('\\', '/');
                    if (relative == RuntimePaths.ManifestFileName ||
                        relative == RuntimePaths.VersionFileName ||
                        relative.EndsWith(".part", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!version.Bundles.ContainsKey(relative))
                    {
                        File.Delete(files[i]);
                        Log.Debug("[HotUpdate] 清理旧包：" + relative);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("[HotUpdate] 清理旧包失败：" + e.Message);
            }
        }
    }
}
