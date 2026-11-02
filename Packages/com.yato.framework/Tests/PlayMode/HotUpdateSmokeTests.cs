using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.Resources;

namespace Yato.Tests.PlayMode
{
    /// <summary>
    /// 热更链路冒烟（T-P5-01~04 实跑）：本地临时目录模拟远端（file://），验证
    /// version.json 拉取 → CRC 差量下载 → manifest 原子替换 → 二次运行全跳过 →
    /// 热更后 manifest 驱动加载 → 首次 CRC 损坏 / 远端不可达不产生远程提交。
    /// 依赖 AB 冒烟同一份打包产物；缺失时整组 Skip。
    /// </summary>
    public sealed class HotUpdateSmokeTests
    {
        private static readonly AssetKey ExampleKey = new AssetKey("ui/example/example");

        private string m_localRoot;
        private string m_remoteRoot;
        private string m_remoteUrl;
        private RemoteDownloader m_downloader;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!File.Exists(RuntimePaths.GetManifestPath(false)))
            {
                Assert.Ignore("未找到 AB 产物，请先打包：Yato/Build/Bundles (Windows)");
                yield break;
            }

            m_localRoot = Path.Combine(Application.temporaryCachePath, "yato-smoke-local-" + Guid.NewGuid().ToString("N"));
            CleanDir(m_localRoot);

            // 远端目录：拷贝打包产物（Application.temporaryCachePath 编辑器/真机通用）
            m_remoteRoot = Path.Combine(Application.temporaryCachePath, "yato-hu-smoke-" + Guid.NewGuid().ToString("N"));
            CleanDir(m_remoteRoot);

            string remotePlatform = Path.Combine(m_remoteRoot, RuntimePaths.PlatformName);
            Directory.CreateDirectory(remotePlatform);
            CopyDir(RuntimePaths.StreamingAssetsBundleDir, remotePlatform);

            m_remoteUrl = new Uri(m_remoteRoot).AbsoluteUri;
            m_downloader = new RemoteDownloader { MaxRetry = 1, RetryDelaySeconds = 0f, AllowFileUrls = true };
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ResourceServiceFactory.ForceBundleMode = false;
            if (!string.IsNullOrEmpty(m_localRoot) && Directory.Exists(m_localRoot)) Directory.Delete(m_localRoot, true);

            if (!string.IsNullOrEmpty(m_remoteRoot) && Directory.Exists(m_remoteRoot))
            {
                Directory.Delete(m_remoteRoot, true);
            }

            m_downloader = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator FreshRun_DownloadsDiff_SecondRun_AllSkipped()
        {
            var expectedBundles = VersionManifest.Parse(File.ReadAllText(Path.Combine(
                m_remoteRoot, RuntimePaths.PlatformName, RuntimePaths.VersionFileName))).Bundles.Count;
            Assert.Greater(expectedBundles, 0, "冒烟产物必须包含 bundle");
            var service = new HotUpdateService(m_downloader, m_localRoot);

            IAwaitable<HotUpdateResult> first = service.RunAsync(m_remoteUrl);
            yield return first.AsEnumerator();

            HotUpdateResult result = first.GetResult();
            Assert.IsTrue(result.Success, "首次热更应成功：" + result);
            Assert.IsTrue(result.Updated, "首次热更应产生下载");
            Assert.AreEqual(expectedBundles, result.DownloadedBundles, "应下载版本清单中的全部 bundle");
            Assert.IsFalse(string.IsNullOrEmpty(result.Version));

            string persistentManifest = Path.Combine(new HotUpdateStorage(m_localRoot).ActiveDirectory, RuntimePaths.ManifestFileName);
            Assert.IsTrue(File.Exists(persistentManifest), "持久化 manifest 应存在");
            Assert.IsTrue(
                File.Exists(BundleRelativePath.Resolve(new HotUpdateStorage(m_localRoot).ActiveDirectory, "ui/example")),
                "持久化 bundle 应存在");

            IAwaitable<HotUpdateResult> second = service.RunAsync(m_remoteUrl);
            yield return second.AsEnumerator();

            HotUpdateResult again = second.GetResult();
            Assert.IsTrue(again.Success, "二次热更应成功：" + again);
            Assert.IsFalse(again.Updated, "无变化时不应产生下载");
            Assert.AreEqual(expectedBundles, again.SkippedBundles, "已校验通过的全部 bundle 应跳过");
            Assert.AreEqual(0, again.DownloadedBundles);
        }

        [UnityTest]
        public IEnumerator HotUpdatedManifest_DrivesLoading()
        {
            // 远端 manifest 抬版本号到 1.0.1，并同步 version.json 的 manifestCrc/appVersion，
            // 热更成功后运行时应读到持久化目录里的 1.0.1（证明热更产物真正驱动了加载）
            string remotePlatform = Path.Combine(m_remoteRoot, RuntimePaths.PlatformName);
            string remoteManifest = Path.Combine(remotePlatform, RuntimePaths.ManifestFileName);
            string edited = File.ReadAllText(remoteManifest)
                .Replace("\"version\": \"1.0.0\"", "\"version\": \"1.0.1\"");
            Assert.AreNotEqual(
                File.ReadAllText(remoteManifest), edited, "内建 manifest 应含 \"version\": \"1.0.0\"");
            File.WriteAllText(remoteManifest, edited);

            string remoteVersion = Path.Combine(remotePlatform, RuntimePaths.VersionFileName);
            // 保留真实产物中的完整 bundle 集合，只更新本测试修改的版本和清单 CRC。
            var editedVersion = Regex.Replace(File.ReadAllText(remoteVersion),
                @"""appVersion""\s*:\s*""[^""]*""", "\"appVersion\":\"1.0.1\"");
            editedVersion = Regex.Replace(editedVersion, @"""manifestCrc""\s*:\s*\d+",
                "\"manifestCrc\":" + Crc32Util.Compute(remoteManifest));
            File.WriteAllText(remoteVersion, editedVersion);

            var service = new HotUpdateService(m_downloader, m_localRoot);
            IAwaitable<HotUpdateResult> updating = service.RunAsync(m_remoteUrl);
            yield return updating.AsEnumerator();
            Assert.IsTrue(updating.GetResult().Success, "热更应成功");

            ResourceServiceFactory.ForceBundleMode = true;
            string active = new HotUpdateStorage(m_localRoot).ActiveDirectory;
            BundleLoader loader = null;
            var counter = new BundleRefCounter(name => loader.Unload(name));
            loader = new BundleLoader(counter, active);
            var provider = new BundleProvider(ResourceManifest.LoadFromFile(Path.Combine(active, "manifest.json")), loader, counter);
            Assert.IsNotNull(provider, "应选中 BundleProvider");
            Assert.AreEqual("1.0.1", provider.Manifest.Version, "应读取持久化目录的热更 manifest");

            IAwaitable<IAssetHandle<GameObject>> loading = new ResourceService(provider)
                .LoadAsync<GameObject>(ExampleKey);
            yield return loading.AsEnumerator();

            IAssetHandle<GameObject> handle = loading.GetResult();
            Assert.IsNotNull(handle.Asset, "热更后的 bundle 应可加载资产");
            handle.Dispose();

            provider.Shutdown();
        }

        [UnityTest]
        public IEnumerator CorruptedCrc_Fails_WithoutCommittingRemote()
        {
            // 远端 version.json 的 bundle CRC 写错 → 下载校验失败 → 失败且不提交活动版本
            string remotePlatform = Path.Combine(m_remoteRoot, RuntimePaths.PlatformName);
            WriteVersionJson(
                Path.Combine(remotePlatform, RuntimePaths.VersionFileName),
                "9.9.9",
                0u,
                "ui/example",
                0xDEADBEEFu,
                new FileInfo(Path.Combine(remotePlatform, "ui/example")).Length);

            var service = new HotUpdateService(m_downloader, m_localRoot);
            IAwaitable<HotUpdateResult> updating = service.RunAsync(m_remoteUrl);
            yield return updating.AsEnumerator();

            HotUpdateResult result = updating.GetResult();
            Assert.IsFalse(result.Success, "CRC 损坏应失败");
            Assert.IsTrue(result.Error != null && result.Error.Contains("CRC"), "应报 CRC 校验失败：" + result.Error);
            Assert.IsFalse(
                File.Exists(Path.Combine(new HotUpdateStorage(m_localRoot).ActiveDirectory, RuntimePaths.ManifestFileName)),
                "首次失败后不得创建远程活动 manifest");
        }

        [UnityTest]
        public IEnumerator UnreachableRemote_Fails_WithoutCommittingRemote()
        {
            string url = new Uri(Path.Combine(m_remoteRoot, "no-such-dir")).AbsoluteUri;

            var service = new HotUpdateService(m_downloader, m_localRoot);
            IAwaitable<HotUpdateResult> updating = service.RunAsync(url);
            yield return updating.AsEnumerator();

            HotUpdateResult result = updating.GetResult();
            Assert.IsFalse(result.Success, "远端不可达应失败");
            Assert.IsFalse(
                File.Exists(Path.Combine(new HotUpdateStorage(m_localRoot).ActiveDirectory, RuntimePaths.ManifestFileName)),
                "首次失败后不得创建远程活动 manifest");
        }

        // ------------------------------------------------------------------

        private static void WriteVersionJson(
            string path, string appVersion, uint manifestCrc, string bundleName, uint bundleCrc, long bundleSize)
        {
            var json = new StringBuilder();
            json.Append("{\"schema\":1");
            json.Append(",\"appVersion\":\"").Append(appVersion).Append('"');
            json.Append(",\"platform\":\"").Append(RuntimePaths.PlatformName).Append('"');
            json.Append(",\"manifestCrc\":").Append(manifestCrc);
            json.Append(",\"bundles\":{\"").Append(bundleName).Append("\":{");
            json.Append("\"hash\":\"x\",\"crc\":").Append(bundleCrc).Append(",\"size\":").Append(bundleSize);
            json.Append("}}}");
            File.WriteAllText(path, json.ToString());
        }

        private static void CleanDir(string dir)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                return;
            }

            foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }
        }

        private static void CopyDir(string from, string to)
        {
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(from.Length).TrimStart('\\', '/');
                string target = Path.Combine(to, relative);
                string targetDir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(file, target, true);
            }
        }
    }
}
