// Included only in manually prepared consumer projects. Runs when --yato-smoke-root <AB root> is supplied.
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yato.App;
using Yato.FullSample;
using Yato.Resources;
using Yato.UI;
using Yato.Config;
using Yato.Pooling;

namespace Yato
{
    public sealed class ConsumerPlayerSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--yato-smoke-root");
            if (index < 0 || index + 1 >= args.Length) return;
            var host = new GameObject("Consumer Player Smoke");
            DontDestroyOnLoad(host);
            host.AddComponent<ConsumerPlayerSmoke>().StartCoroutine(RunGuarded(args[index + 1]));
        }

        private static IEnumerator RunGuarded(string remoteRoot)
        {
            var routine = Run(remoteRoot);
            while (true)
            {
                bool next;
                try { next = routine.MoveNext(); }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    Application.Quit(1);
                    yield break;
                }
                if (!next) break;
                yield return routine.Current;
            }
            Debug.Log("[Yato.ConsumerPlayerSmoke] PASS: Bootstrap, UI, Single/Additive scenes, Settings, Audio, Flow, Config, Pooling, transactional AB update and shutdown");
            Application.Quit(0);
        }

        private static IEnumerator Run(string remoteRoot)
        {
            yield return null;
            Check(GameBootstrap.IsInitialized, "Bootstrap");
            var opening = UIRouter.OpenAsync(new UIPageDef("PlayerBasic", null, new AssetKey("yato/basic/page")));
            while (!opening.IsDone) yield return null;
            var handle = opening.GetResult();
            Check(handle.IsValid, "UI");
            var scene = GameBootstrap.Scene.LoadAsync(new SceneLoadRequest("YatoBasicSecond"));
            while (!scene.IsDone) yield return null;
            Check(scene.GetResult().Status == SceneLoadStatus.Success, "Single scene");
            Check(!handle.IsValid, "Old page release");
            var additive = GameBootstrap.Scene.LoadAsync(new SceneLoadRequest("YatoBasic", LoadSceneMode.Additive));
            while (!additive.IsDone) yield return null;
            Check(additive.GetResult().Status == SceneLoadStatus.Success, "Additive scene");
            GameBootstrap.Shutdown();
            var full = SceneManager.LoadSceneAsync("YatoFull", LoadSceneMode.Single); // yato:allow scene-load-direct -- test starts a fresh bootstrap after shutdown
            while (!full.isDone) yield return null;
            yield return null;
            Check(GameBootstrap.IsInitialized, "Full Bootstrap");
            var foundation = UnityEngine.Object.FindObjectOfType<FullSampleEntry>();
            float deadline = Time.realtimeSinceStartup + 5;
            while (foundation.DemoPool == null && Time.realtimeSinceStartup < deadline) yield return null;
            Check(foundation.DemoPool != null, "Foundation configuration and pool");
            Check(foundation.DemoConfig.Get<DemoConfigRecord>("demo").PoolCapacity == 8, "Config asset");
            var instances = new PooledInstance[foundation.DemoPool.Capacity];
            for (int i = 0; i < instances.Length; i++) Check(foundation.DemoPool.TryRent(out instances[i]), "Pool capacity");
            Check(!foundation.DemoPool.TryRent(out _), "Pool rejects overflow");
            foreach (var item in instances) item.Dispose();
            for (int i = 0; i < 100; i++) { foundation.DemoPool.TryRent(out var item); item.Dispose(); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { foundation.DemoPool.TryRent(out var item); item.Dispose(); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "Pool warm allocations: " + allocated);
            float totalMs = 0, maxMs = 0;
            for (int i = 0; i < 60; i++) { yield return null; float ms = Time.unscaledDeltaTime * 1000; totalMs += ms; maxMs = Mathf.Max(maxMs, ms); }
            Debug.Log("[Yato.Performance] poolPeak=" + foundation.DemoPool.PeakActiveCount +
                " created=" + foundation.DemoPool.CreatedCount + " warm1000Bytes=" + allocated +
                " frameSamples=60 meanMs=" + (totalMs / 60).ToString("F3") + " maxMs=" + maxMs.ToString("F3"));
            var stopped = foundation.StopFoundationDemo();
            while (!stopped.IsDone) yield return null;
            Check(stopped.GetResult().Status == Yato.Flow.FlowStatus.Success, "Flow exit");
            var service = new FullSampleService();
            service.Initialize();
            service.ApplyVolume(0.35f);
            Check(Math.Abs(service.Volume - 0.35f) < 0.001f, "Settings Apply");
            service.PlayTone();
            service.Shutdown();
            service.Shutdown();

            string local = Path.Combine(Application.temporaryCachePath, "yato-player-smoke-" + Guid.NewGuid().ToString("N"));
            var update = new HotUpdateService(new RemoteDownloader { AllowFileUrls = true }, local)
                .RunAsync(new Uri(Path.GetFullPath(remoteRoot)).AbsoluteUri);
            while (!update.IsDone) yield return null;
            Check(update.GetResult().Success, update.GetResult().Error);
            string active = new HotUpdateStorage(local).ActiveDirectory;
            var creating = BundleResourceServiceFactory.CreateAsync(manifestPath: Path.Combine(active, "manifest.json"));
            while (!creating.IsDone) yield return null;
            var resources = creating.GetResult();
            try
            {
                var loading = resources.LoadAsync<GameObject>(new AssetKey("ui/example/example"));
                while (!loading.IsDone) yield return null;
                var asset = loading.GetResult();
                Check(asset.Asset != null, "Updated AB load");
                asset.Dispose();
            }
            finally { (resources as IDisposable)?.Dispose(); }
            UIRouter.CloseAll();
            GameBootstrap.Shutdown();
            Check(!GameBootstrap.IsInitialized, "Shutdown");
            var oldBootstrap = GameBootstrap.Instance;
            if (oldBootstrap != null) UnityEngine.Object.Destroy(oldBootstrap.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync("Bootstrap"); // yato:allow scene-load-direct -- test creates the starter bootstrap after destroying the old host
            deadline = Time.realtimeSinceStartup + 5;
            while (UIRouter.Stack.Find("Main") == null && Time.realtimeSinceStartup < deadline) yield return null;
            Check(GameBootstrap.IsInitialized && UIRouter.Stack.Find("Main") != null, "Generated starter catalog");
            var starterPage = UnityEngine.Object.FindAnyObjectByType<UIPageView>();
            Check(starterPage != null, "Generated starter view");
            starterPage.GetComponentInChildren<UnityEngine.UI.Button>(true).onClick.Invoke();
            yield return null;
            Check(UIRouter.Stack.Find("Main") == null, "Generated starter button");
            Check(UIRouter.Resources.Stats.ActiveAssetCount == 0, "Generated starter resource release");
            GameBootstrap.Shutdown();
        }
        private static void Check(bool condition, string stage)
        {
            if (!condition) throw new InvalidOperationException("Consumer Player smoke failed: " + stage);
        }
    }
}
