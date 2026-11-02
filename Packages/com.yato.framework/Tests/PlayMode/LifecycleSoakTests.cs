using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Yato.App;
using Yato.Core;
using Yato.Flow;
using Yato.Pooling;
using Yato.Resources;
using Yato.UI;

namespace Yato.Tests.PlayMode
{
    public sealed class LifecycleSoakTests
    {
        private sealed class Provider : IResourceProvider
        {
            public GameObject PoolPrefab, PagePrefab;
            public ResourceStats Stats { get; } = new ResourceStats();
            public IAwaitable<UnityEngine.Object> LoadAsync(AssetKey key, Type type, CancellationToken ct)
            {
                var source = new Yato.Resources.AwaitableCompletionSource<UnityEngine.Object>();
                source.SetResult(key.ToString() == "soak/pool" ? PoolPrefab : PagePrefab);
                return source;
            }
            public void Release(AssetKey key) { }
            public void ReleaseUnused() { }
        }
        private sealed class Cleanup : IDisposable
        {
            private Action m_action;
            public Cleanup(Action action) { m_action = action; }
            public void Dispose() { var action = m_action; m_action = null; action?.Invoke(); }
        }
        private sealed class State : IFlowState
        {
            public string Id { get; }
            public IResourceService Resources;
            public Transform Parent;
            public PauseService Pause;
            public ResourceInstancePool Pool;
            public State(string id) { Id = id; }
            public IAwaitable EnterAsync(FlowContext context, CancellationToken ct) => Enter(context, ct).AsAwaitable();
            private async Task Enter(FlowContext context, CancellationToken ct)
            {
                if (Id == "idle") return;
                Pool = context.Resources.Track(await ResourceInstancePool.CreateAsync(Resources, new AssetKey("soak/pool"), 4, Parent, ct));
                Pool.Prewarm(4);
                var page = await UIRouter.OpenAsync(new UIPageDef("LifecycleSoak", null, new AssetKey("soak/page")));
                context.Resources.Track(new Cleanup(page.Close));
                context.Resources.Track(Pause.Acquire("soak"));
            }
            public void Exit(FlowContext context) { Pool = null; }
        }
        [UnityTest] public IEnumerator HundredSceneFlowUiPoolCycles_ReturnToResourceAndDiagnosticBaselines()
        {
            var poolPrefab = new GameObject("Soak pool prefab"); poolPrefab.SetActive(false);
            var pagePrefab = new GameObject("Soak page prefab", typeof(RectTransform), typeof(UIPageView)); pagePrefab.SetActive(false);
            var service = new ResourceService(new Provider { PoolPrefab = poolPrefab, PagePrefab = pagePrefab });
            UIRouter.Configure(service);
            int baselineResources = ResourceTracker.Count, baselineDiagnostics = DiagnosticRegistry.Capture().Count, peakResources = baselineResources;
            float originalScale = Time.timeScale;
            try
            {
                for (int cycle = 0; cycle < 100; cycle++)
                {
                    var scene = SceneManager.CreateScene("YatoSoak_" + cycle);
                    var parent = new GameObject("Scene pool root"); SceneManager.MoveGameObjectToScene(parent, scene);
                    try
                    {
                        using (var pause = new PauseService())
                        {
                            var ready = new State("ready") { Resources = service, Parent = parent.transform, Pause = pause };
                            using (var flow = new FlowController(new IFlowState[] { ready, new State("idle") }))
                            {
                                var entering = flow.SwitchAsync("ready");
                                yield return entering.AsEnumerator();
                                Assert.AreEqual(FlowStatus.Success, entering.GetResult().Status, entering.GetResult().Error?.ToString());
                                Assert.IsTrue(ready.Pool.TryRent(out var rental));
                                Assert.IsTrue(pause.IsPaused);
                                Assert.IsTrue(DiagnosticRegistry.Capture().Any(x => x.Module == "Pooling" && x.Values["Active"] == "1"));
                                peakResources = Math.Max(peakResources, ResourceTracker.Count);
                                var unloading = SceneManager.UnloadSceneAsync(scene);
                                while (!unloading.isDone) yield return null;
                                Assert.IsFalse(rental.IsValid);
                                var exit = flow.SwitchAsync("idle");
                                yield return exit.AsEnumerator();
                                Assert.AreEqual(FlowStatus.Success, exit.GetResult().Status);
                                Assert.IsFalse(pause.IsPaused);
                                Assert.AreEqual(originalScale, Time.timeScale);
                                Assert.IsNull(UIRouter.Stack.Find("LifecycleSoak"));
                                Assert.AreEqual(baselineResources, ResourceTracker.Count, "Resource baseline at cycle " + cycle);
                            }
                        }
                        Assert.AreEqual(baselineDiagnostics, DiagnosticRegistry.Capture().Count, "Diagnostic registration baseline at cycle " + cycle);
                    }
                    finally { if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene); }
                }
                Debug.Log("[Yato.Soak] cycles=100 resourceBaseline=" + baselineResources + " resourcePeak=" + peakResources + " diagnosticsBaseline=" + baselineDiagnostics);
            }
            finally
            {
                UIRouter.CloseAll();
                Time.timeScale = originalScale;
                UnityEngine.Object.Destroy(poolPrefab); UnityEngine.Object.Destroy(pagePrefab);
            }
        }
    }
}
