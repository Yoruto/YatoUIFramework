using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Yato.App;
using Yato.BasicSample;
using Yato.FullSample;
using Yato.Resources;
using Yato.UI;

namespace Yato.ConsumerTests
{
    public sealed class ConsumerSmokeTests
    {
        [UnityTest] public IEnumerator StandaloneSamples_OpenPagesAndReleaseOnSceneSwitch()
        {
            yield return SceneManager.LoadSceneAsync("YatoBasic");
            Assert.IsNotNull(Object.FindObjectOfType<BasicSampleEntry>());
            Assert.IsTrue(GameBootstrap.IsInitialized);
            var page = UIRouter.OpenAsync(new UIPageDef("ConsumerBasic", null, new AssetKey("yato/basic/page")));
            yield return page.AsEnumerator();
            var handle = page.GetResult();
            Assert.IsTrue(handle.IsValid);
            yield return GameBootstrap.Scene.LoadAsync(new SceneLoadRequest("YatoBasicSecond")).AsEnumerator();
            Assert.IsFalse(handle.IsValid);
            Assert.IsTrue(GameBootstrap.IsInitialized);
            GameBootstrap.Shutdown();
            yield return SceneManager.LoadSceneAsync("YatoFull");
            Assert.IsNotNull(Object.FindObjectOfType<FullSampleEntry>());
            Assert.IsTrue(GameBootstrap.IsInitialized);
            var full = UIRouter.OpenAsync(new UIPageDef("ConsumerFull", null, new AssetKey("yato/full/page")));
            yield return full.AsEnumerator();
            Assert.IsTrue(full.GetResult().IsValid);
            UIRouter.CloseAll();
            Assert.IsFalse(full.GetResult().IsValid);
            var scenes = GameBootstrap.Scene;
            var startedField = scenes.GetType().GetField("LoadStarted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var completedField = scenes.GetType().GetField("LoadCompleted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int beforeStarted = ((System.Delegate)startedField.GetValue(scenes))?.GetInvocationList().Length ?? 0;
            int beforeCompleted = ((System.Delegate)completedField.GetValue(scenes))?.GetInvocationList().Length ?? 0;
            var service = new FullSampleService();
            for (int i = 0; i < 3; i++)
            {
                service.Initialize();
                service.Initialize();
                Assert.AreEqual(beforeStarted + 1, ((System.Delegate)startedField.GetValue(scenes)).GetInvocationList().Length);
                service.Shutdown();
                service.Shutdown();
                Assert.AreEqual(beforeStarted, ((System.Delegate)startedField.GetValue(scenes))?.GetInvocationList().Length ?? 0);
                Assert.AreEqual(beforeCompleted, ((System.Delegate)completedField.GetValue(scenes))?.GetInvocationList().Length ?? 0);
            }
            GameBootstrap.Shutdown();
        }
    }
}
