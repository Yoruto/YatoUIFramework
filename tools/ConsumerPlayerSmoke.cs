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
            Debug.Log("[Yato.ConsumerPlayerSmoke] PASS: Bootstrap, UI, Single/Additive scenes, Settings, Audio, transactional AB update and shutdown");
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
            var full = SceneManager.LoadSceneAsync("YatoFull", LoadSceneMode.Single);
            while (!full.isDone) yield return null;
            yield return null;
            Check(GameBootstrap.IsInitialized, "Full Bootstrap");
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
            BundleLoader loader = null;
            var counter = new BundleRefCounter(name => loader.Unload(name));
            loader = new BundleLoader(counter, active);
            var provider = new BundleProvider(ResourceManifest.LoadFromFile(Path.Combine(active, "manifest.json")), loader, counter);
            var loading = new ResourceService(provider).LoadAsync<GameObject>(new AssetKey("ui/example/example"));
            while (!loading.IsDone) yield return null;
            var asset = loading.GetResult();
            Check(asset.Asset != null, "Updated AB load");
            asset.Dispose();
            provider.Shutdown();
            UIRouter.CloseAll();
            GameBootstrap.Shutdown();
            Check(!GameBootstrap.IsInitialized, "Shutdown");
        }
        private static void Check(bool condition, string stage)
        {
            if (!condition) throw new InvalidOperationException("Consumer Player smoke failed: " + stage);
        }
    }
}
