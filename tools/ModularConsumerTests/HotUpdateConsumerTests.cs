using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.Resources;

namespace Yato.ModularConsumerTests
{
    public sealed class HotUpdateConsumerTests
    {
        [UnityTest]
        public IEnumerator OptionalUpdaterCommitsAndLoadsARealVerifiedBundleSnapshot()
        {
            string root = Path.Combine(Application.temporaryCachePath, "yato-modular-update-" + Guid.NewGuid().ToString("N"));
            string remote = Path.Combine(Application.streamingAssetsPath, "YatoModular/AssetBundles");
            Assert.IsTrue(File.Exists(Path.Combine(remote, RuntimePaths.PlatformName, "version.json")));
            IResourceService service = null;
            try
            {
                HotUpdateBundleCachePolicy.Install();
                var update = new HotUpdateService(new RemoteDownloader { AllowFileUrls = true }, root)
                    .RunAsync(new Uri(Path.GetFullPath(remote)).AbsoluteUri);
                yield return update.AsEnumerator();
                var result = update.GetResult();
                Assert.IsTrue(result.Success, result.Error);
                Assert.AreEqual(1, result.DownloadedBundles);
                string active = new HotUpdateStorage(root).ActiveDirectory;
                Assert.AreEqual(active, BundleCachePolicy.Current.GetActiveDirectory(root));
                Assert.IsTrue(File.Exists(Path.Combine(root, HotUpdateStorage.PointerFile)));
                var creating = BundleResourceServiceFactory.CreateAsync(manifestPath: Path.Combine(active, "manifest.json"));
                yield return creating.AsEnumerator();
                service = creating.GetResult();
                var loading = service.LoadAsync<GameObject>(new AssetKey("yato_modular_fixture"));
                yield return loading.AsEnumerator();
                var asset = loading.GetResult();
                Assert.IsNotNull(asset.Asset);
                asset.Dispose();
                Assert.AreEqual(0, service.Stats.ActiveAssetCount);
            }
            finally
            {
                (service as IDisposable)?.Dispose();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
