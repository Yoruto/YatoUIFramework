using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.Resources;

namespace Yato.ModularConsumerTests
{
    public sealed class BundleConsumerTests
    {
        [UnityTest]
        public IEnumerator ExplicitBundleFactoryLoadsARealLocalBundleAndDisposesIt()
        {
            string manifest = Path.Combine(Application.streamingAssetsPath, "YatoModular/AssetBundles", RuntimePaths.PlatformName, "manifest.json");
            Assert.IsTrue(File.Exists(manifest), "Run Yato.ModularConsumerVerification.PrepareAndVerify first");
            var creating = BundleResourceServiceFactory.CreateAsync(manifestPath: manifest);
            yield return creating.AsEnumerator();
            var service = creating.GetResult();
            try
            {
                var loading = service.LoadAsync<GameObject>(new AssetKey("yato_modular_fixture"));
                yield return loading.AsEnumerator();
                var asset = loading.GetResult();
                Assert.AreEqual("yato_modular_fixture", asset.Asset.name);
                Assert.AreEqual(1, service.Stats.LoadedBundleCount);
                asset.Dispose();
                service.ReleaseUnused();
                Assert.AreEqual(0, service.Stats.ActiveAssetCount);
                // Ordinary releases retain the established unload delay; disposing the owner
                // closes the provider immediately, including its delayed bundle references.
                (service as IDisposable)?.Dispose();
                Assert.AreEqual(0, service.Stats.LoadedBundleCount);
            }
            finally { (service as IDisposable)?.Dispose(); }
        }
    }
}
