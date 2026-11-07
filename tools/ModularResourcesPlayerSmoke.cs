// Copied only to the two-package ResourcesOnly consumer. Enable with --yato-modular-smoke.
using System;
using System.Collections;
using UnityEngine;
using Yato.Resources;

namespace Yato
{
    public sealed class ModularResourcesPlayerSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--yato-modular-smoke") < 0) return;
            var host = new GameObject("ResourcesOnly Player smoke");
            host.AddComponent<ModularResourcesPlayerSmoke>().StartCoroutine(RunGuarded());
        }

        private static IEnumerator RunGuarded()
        {
            var routine = Run();
            try
            {
                while (true)
                {
                    bool more;
                    try { more = routine.MoveNext(); }
                    catch (Exception error)
                    {
                        Debug.LogException(error);
                        Application.Quit(1);
                        yield break;
                    }
                    if (!more) break;
                    yield return routine.Current;
                }
            }
            finally { (routine as IDisposable)?.Dispose(); }
            Debug.Log("[Yato.ModularResourcesPlayerSmoke] PASS: default ResourcesProvider, real prefab load and handle release");
            Application.Quit(0);
        }

        private static IEnumerator Run()
        {
            var service = (ResourceService)ResourceServiceFactory.Create();
            try
            {
                if (!(service.Provider is ResourcesProvider))
                    throw new InvalidOperationException("ResourcesOnly Player default factory did not select ResourcesProvider");
                var loading = service.LoadAsync<GameObject>(new AssetKey("yato_modular_fixture"));
                float deadline = Time.realtimeSinceStartup + 15f;
                while (!loading.IsDone && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loading.IsDone) throw new TimeoutException("ResourcesOnly prefab load exceeded 15 seconds");
                using (var handle = loading.GetResult())
                {
                    if (handle.Asset == null || handle.Asset.name != "yato_modular_fixture")
                        throw new InvalidOperationException("ResourcesOnly prefab was not loaded");
                    if (service.Stats.ActiveAssetCount != 1)
                        throw new InvalidOperationException("ResourcesOnly prefab handle was not tracked");
                }
                if (service.Stats.ActiveAssetCount != 0)
                    throw new InvalidOperationException("ResourcesOnly prefab handle was not released");
            }
            finally { service.Dispose(); }
        }
    }
}
