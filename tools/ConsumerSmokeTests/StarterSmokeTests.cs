using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Yato.App;
using Yato.Resources;
using Yato.UI;

namespace Yato.ConsumerTests
{
    public sealed class StarterSmokeTests
    {
        [UnityTest] public IEnumerator GeneratedBootstrap_LoadsCatalogAndClosesItsRealButton()
        {
            UIRouter.CloseAll();
            var old = GameBootstrap.Instance;
            GameBootstrap.Shutdown();
            if (old != null) Object.Destroy(old.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync("Bootstrap"); // yato:allow scene-load-direct -- test starts the generated bootstrap after destroying the old host
            float deadline = Time.realtimeSinceStartup + 5;
            while (UIRouter.Stack.Find("Main") == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(GameBootstrap.IsInitialized);
            Assert.IsNotNull(UIRouter.Stack.Find("Main"));
            var page = Object.FindAnyObjectByType<UIPageView>();
            Assert.IsNotNull(page);
            page.GetComponentInChildren<Button>(true).onClick.Invoke();
            yield return null;
            Assert.IsNull(UIRouter.Stack.Find("Main"));
            Assert.AreEqual(0, ResourceTracker.Count);
            Assert.AreEqual(0, UIRouter.Resources.Stats.ActiveAssetCount);
        }
    }
}
