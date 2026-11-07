using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Yato.Resources;
using Yato.UI;

namespace Yato.ModularConsumerTests
{
    public sealed class UIConsumerTests
    {
        [UnityTest]
        public IEnumerator MinimalUIOpensAndReleasesARealResourcesPrefab()
        {
            var service = ResourceServiceFactory.Create(new ResourcesProvider(), true);
            try
            {
                UIRouter.Configure(service);
                var loading = UIRouter.OpenAsync(new UIPageDef("ModularPage", null, new AssetKey("yato_modular_fixture")));
                yield return loading.AsEnumerator();
                var page = loading.GetResult();
                Assert.IsTrue(page.IsValid);
                Assert.AreEqual(1, service.Stats.ActiveAssetCount);
                UIRouter.Close(page);
                Assert.IsFalse(page.IsValid);
                Assert.AreEqual(0, service.Stats.ActiveAssetCount);
                Assert.AreEqual(0, UIRouter.Stack.GetAllHandles().Count);
            }
            finally
            {
                UIRouter.CloseAll();
                UIRouter.Configure(NullResourceService.Instance);
                (service as IDisposable)?.Dispose();
            }
        }
    }
}
