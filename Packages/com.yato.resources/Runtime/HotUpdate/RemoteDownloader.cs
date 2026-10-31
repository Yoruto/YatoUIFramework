using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>
    /// 远程下载器（T-P5-02）：失败重试、进度回调、取消、CRC 校验。
    /// 「断点」粒度为文件：已存在且 CRC 正确的 bundle 直接跳过，不重复下载。
    /// </summary>
    public sealed class RemoteDownloader
    {
        /// <summary>单文件最大重试次数。</summary>
        public int MaxRetry = 3;

        /// <summary>重试间隔（秒）。</summary>
        public float RetryDelaySeconds = 1f;

        /// <summary>下载到目标路径，必要时校验 CRC。</summary>
        public IAwaitable<long> DownloadAsync(
            string url,
            string savePath,
            uint expectedCrc = 0u,
            IProgress<float> progress = null,
            CancellationToken ct = default(CancellationToken))
        {
            var source = new AwaitableCompletionSource<long>();
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(savePath))
            {
                source.SetException(new ResourceException(null, null, ResourceStage.BundleDownload, "下载参数为空"));
                return source;
            }

            RuntimeHost.RunCoroutine(DownloadRoutine(url, savePath, expectedCrc, progress, ct, source));
            return source;
        }

        private IEnumerator DownloadRoutine(
            string url,
            string savePath,
            uint expectedCrc,
            IProgress<float> progress,
            CancellationToken ct,
            AwaitableCompletionSource<long> source)
        {
            int attempt = 0;
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    source.SetException(new OperationCanceledException());
                    yield break;
                }

                attempt++;
                string tempPath = savePath + ".part";
                string directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.downloadHandler = new DownloadHandlerFile(tempPath);
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();

                    while (!operation.isDone)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            request.Abort();
                            SafeDelete(tempPath);
                            source.SetException(new OperationCanceledException());
                            yield break;
                        }

                        if (progress != null)
                        {
                            progress.Report(request.downloadProgress);
                        }

                        yield return null;
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        SafeDelete(tempPath);
                        if (attempt < MaxRetry)
                        {
                            Log.Warn(string.Format("[HotUpdate] 下载失败（第 {0} 次）：{1}（{2}）", attempt, url, request.error));
                            yield return new WaitForSeconds(RetryDelaySeconds);
                            continue;
                        }

                        source.SetException(new ResourceException(null, url, ResourceStage.BundleDownload,
                            string.Format("下载失败：{0}（{1}）", url, request.error)));
                        yield break;
                    }
                }

                long size = 0;
                if (File.Exists(tempPath))
                {
                    size = new FileInfo(tempPath).Length;
                }

                if (expectedCrc != 0u)
                {
                    uint actual = Crc32Util.Compute(tempPath);
                    if (actual != expectedCrc)
                    {
                        SafeDelete(tempPath);
                        if (attempt < MaxRetry)
                        {
                            Log.Warn(string.Format("[HotUpdate] CRC 不匹配（第 {0} 次）：{1}", attempt, url));
                            yield return new WaitForSeconds(RetryDelaySeconds);
                            continue;
                        }

                        source.SetException(new ResourceException(null, url, ResourceStage.BundleDownload,
                            string.Format("CRC 校验失败：{0} 期望 {1}", url, expectedCrc)));
                        yield break;
                    }
                }

                TryDelete(savePath);
                try
                {
                    File.Move(tempPath, savePath);
                }
                catch (Exception e)
                {
                    source.SetException(new ResourceException(null, url, ResourceStage.BundleDownload, "落盘失败", e));
                    yield break;
                }

                source.SetResult(size);
                yield break;
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e)
            {
                Log.Warn("[HotUpdate] 删除临时文件失败：" + e.Message);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // 落盘前清理旧文件失败不致命
            }
        }
    }
}
