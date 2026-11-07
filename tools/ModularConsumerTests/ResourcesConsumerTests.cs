using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.Resources;

namespace Yato.ModularConsumerTests
{
    public sealed class ResourcesConsumerTests
    {
        private static readonly AssetKey Key = new AssetKey("yato_modular_fixture");

        [UnityTest]
        public IEnumerator DefaultFactoryAndResourcesProviderLoadWithoutOptionalModules()
        {
            var service = ResourceServiceFactory.Create();
            try
            {
                var first = service.LoadAsync<GameObject>(Key);
                var second = service.LoadAsync<GameObject>(Key);
                yield return first.AsEnumerator();
                yield return second.AsEnumerator();
                var a = first.GetResult(); var b = second.GetResult();
                Assert.AreSame(a.Asset, b.Asset);
                Assert.AreEqual("yato_modular_fixture", a.Asset.name);
                a.Dispose();
                Assert.AreEqual(1, service.Stats.ActiveAssetCount);
                b.Dispose();
                Assert.AreEqual(0, service.Stats.ActiveAssetCount);
            }
            finally { (service as IDisposable)?.Dispose(); }

            service = ResourceServiceFactory.Create(new ResourcesProvider(), true);
            try
            {
                var loading = service.LoadAsync<GameObject>(Key);
                yield return loading.AsEnumerator();
                var asset = loading.GetResult();
                Assert.IsNotNull(asset.Asset, "Real Resources load must work in a two-package consumer");
                asset.Dispose();
                Assert.AreEqual(0, service.Stats.ActiveAssetCount);
            }
            finally { (service as IDisposable)?.Dispose(); }
        }
    }
}
