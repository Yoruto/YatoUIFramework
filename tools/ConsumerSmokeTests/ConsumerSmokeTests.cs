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
using Yato.Config;
using Yato.Pooling;

namespace Yato.ConsumerTests
{
    public sealed class ConsumerSmokeTests
    {
        [UnityTest] public IEnumerator StandaloneSamples_OpenPagesAndReleaseOnSceneSwitch()
        {
            yield return SceneManager.LoadSceneAsync("YatoBasic"); // yato:allow scene-load-direct -- test fixture selects its initial scene
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
            yield return SceneManager.LoadSceneAsync("YatoFull"); // yato:allow scene-load-direct -- test restarts the sample after shutdown
            Assert.IsNotNull(Object.FindObjectOfType<FullSampleEntry>());
            Assert.IsTrue(GameBootstrap.IsInitialized);
            var full = UIRouter.OpenAsync(new UIPageDef("ConsumerFull", null, new AssetKey("yato/full/page")));
            yield return full.AsEnumerator();
            Assert.IsTrue(full.GetResult().IsValid);
            UIRouter.CloseAll();
            Assert.IsFalse(full.GetResult().IsValid);
            var entry = Object.FindObjectOfType<FullSampleEntry>();
            float deadline = Time.realtimeSinceStartup + 5;
            while (entry.DemoPool == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsNotNull(entry.DemoPool, "Foundation flow must load the real sample catalog");
            Assert.AreEqual(8, entry.DemoConfig.Get<DemoConfigRecord>("demo").PoolCapacity);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                if (cycle > 0) yield return entry.StartFoundationDemo().AsEnumerator();
                var pool = entry.DemoPool;
                Assert.AreEqual(8, pool.CreatedCount);
                Assert.IsTrue(pool.TryRent(out var lease));
                yield return entry.StopFoundationDemo().AsEnumerator();
                Assert.IsFalse(lease.IsValid);
                Assert.IsNull(entry.DemoPool);
                yield return null;
                Assert.AreEqual(0, Yato.Resources.ResourceTracker.Count, "No resource handles after stopping foundation demo");
            }
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
