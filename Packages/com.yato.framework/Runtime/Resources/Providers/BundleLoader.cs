using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>
    /// 单个 bundle 的加载器：单飞（同一 bundle 并发请求只发一次）、平台分流、
    /// 失败统一抛 <see cref="ResourceException"/>（RES-01/RES-04 的修复）。
    /// </summary>
    public sealed class BundleLoader
    {
        private readonly Dictionary<string, AssetBundle> m_loaded =
            new Dictionary<string, AssetBundle>(StringComparer.Ordinal);

        private readonly Dictionary<string, IAwaitable> m_pending =
            new Dictionary<string, IAwaitable>(StringComparer.Ordinal);

        private readonly BundleRefCounter m_refCounter;

        public BundleLoader(BundleRefCounter refCounter)
        {
            if (refCounter == null)
            {
                throw new ArgumentNullException("refCounter");
            }

            m_refCounter = refCounter;
        }

        /// <summary>已加载的 bundle（未加载返回 null）。</summary>
        public AssetBundle GetBundle(string bundleName)
        {
            AssetBundle bundle;
            return !string.IsNullOrEmpty(bundleName) && m_loaded.TryGetValue(bundleName, out bundle) ? bundle : null;
        }

        /// <summary>加载 bundle（幂等、单飞）。</summary>
        public IAwaitable LoadAsync(string bundleName, CancellationToken ct)
        {
            var immediate = new AwaitableCompletionSource();

            if (string.IsNullOrEmpty(bundleName))
            {
                immediate.SetException(new ResourceException(null, null, ResourceStage.BundleLoad, "bundle 名为空"));
                return immediate;
            }

            if (m_loaded.ContainsKey(bundleName))
            {
                immediate.SetResult();
                return immediate;
            }

            IAwaitable existing;
            if (m_pending.TryGetValue(bundleName, out existing))
            {
                return existing;
            }

            IAwaitable loading = LoadInternal(bundleName, ct);
            m_pending[bundleName] = loading;

            loading.Completed += delegate
            {
                m_pending.Remove(bundleName);
            };

            return loading;
        }

        /// <summary>卸载 bundle：只允许 <c>Unload(false)</c>（RES-05）。</summary>
        public void Unload(string bundleName)
        {
            AssetBundle bundle;
            if (!m_loaded.TryGetValue(bundleName, out bundle))
            {
                return;
            }

            m_loaded.Remove(bundleName);

            if (bundle == null)
            {
                return;
            }

            try
            {
                bundle.Unload(false);
            }
            catch (Exception e)
            {
                Log.Error("BundleLoader", "卸载失败：" + bundleName, e);
            }
        }

        /// <summary>卸载全部（Shutdown 时才允许 Unload(true)）。</summary>
        public void UnloadAll(bool unloadAllLoadedObjects = false)
        {
            foreach (KeyValuePair<string, AssetBundle> pair in m_loaded)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                try
                {
                    pair.Value.Unload(unloadAllLoadedObjects);
                }
                catch (Exception e)
                {
                    Log.Error("BundleLoader", "卸载失败：" + pair.Key, e);
                }
            }

            m_loaded.Clear();
        }

        private IAwaitable LoadInternal(string bundleName, CancellationToken ct)
        {
            var source = new AwaitableCompletionSource();
            string path;

            try
            {
                path = RuntimePaths.ResolveBundlePath(bundleName);
            }
            catch (Exception e)
            {
                source.SetException(new ResourceException(null, bundleName, ResourceStage.BundleLoad, "解析 bundle 路径失败", e));
                return source;
            }

            if (RuntimePaths.RequiresWebRequest)
            {
                RuntimeHost.RunCoroutine(LoadByWebRequest(bundleName, path, source, ct));
            }
            else
            {
                LoadFromFile(bundleName, path, source);
            }

            return source;
        }

        private void LoadFromFile(string bundleName, string path, AwaitableCompletionSource source)
        {
            AssetBundleCreateRequest request;
            try
            {
                request = AssetBundle.LoadFromFileAsync(path);
            }
            catch (Exception e)
            {
                source.SetException(new ResourceException(null, bundleName, ResourceStage.BundleLoad, "LoadFromFile 失败：" + path, e));
                return;
            }

            if (request == null)
            {
                source.SetException(new ResourceException(null, bundleName, ResourceStage.BundleLoad, "LoadFromFile 返回空：" + path));
                return;
            }

            request.completed += delegate
            {
                try
                {
                    AssetBundle bundle = request.assetBundle;
                    if (bundle == null)
                    {
                        throw new ResourceException(null, bundleName, ResourceStage.BundleLoad, "bundle 加载结果为空：" + path);
                    }

                    Register(bundleName, bundle);
                    source.SetResult();
                }
                catch (Exception e)
                {
                    source.SetException(e);
                }
            };
        }

        private IEnumerator LoadByWebRequest(string bundleName, string path, AwaitableCompletionSource source, CancellationToken ct)
        {
            string url = RuntimePaths.ToLoadPath(path);
            using (UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(url))
            {
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (ct.IsCancellationRequested)
                    {
                        request.Abort();
                        source.SetException(new OperationCanceledException());
                        yield break;
                    }

                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    source.SetException(new ResourceException(
                        null,
                        bundleName,
                        ResourceStage.BundleLoad,
                        string.Format("下载失败：{0}（{1}）", url, request.error)));
                    yield break;
                }

                try
                {
                    AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(request);
                    if (bundle == null)
                    {
                        throw new ResourceException(null, bundleName, ResourceStage.BundleLoad, "下载结果为空：" + url);
                    }

                    Register(bundleName, bundle);
                    source.SetResult();
                }
                catch (Exception e)
                {
                    source.SetException(e);
                }
            }
        }

        private void Register(string bundleName, AssetBundle bundle)
        {
            m_loaded[bundleName] = bundle;
            m_refCounter.MarkLoaded(bundleName, 0, 0f);
            Log.Debug(string.Format("[BundleLoader] 已加载 {0}", bundleName));
        }
    }
}
