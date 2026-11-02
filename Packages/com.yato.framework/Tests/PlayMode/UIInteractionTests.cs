using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.Resources;
using Yato.UI;

namespace Yato.Tests.PlayMode
{
    public sealed class UIInteractionTests
    {
        private GameObject m_prefab;
        private float m_scale;
        private sealed class Provider : IResourceProvider
        {
            public GameObject Prefab;
            public int Released;
            public ResourceStats Stats { get; } = new ResourceStats();
            public IAwaitable<UnityEngine.Object> LoadAsync(AssetKey key, Type type, CancellationToken ct)
            { var result = new Yato.Resources.AwaitableCompletionSource<UnityEngine.Object>(); result.SetResult(Prefab); return result; }
            public void Release(AssetKey key) { Released++; }
            public void ReleaseUnused() { }
        }
        [SetUp] public void Setup()
        {
            UIRouter.CloseAll(); m_scale = Time.timeScale;
            m_prefab = new GameObject("Transition prefab", typeof(RectTransform)); m_prefab.SetActive(false);
            m_prefab.AddComponent<UIPageView>();
        }
        [TearDown] public void Teardown()
        {
            Time.timeScale = m_scale; UIRouter.CloseAll(); UIRouter.Configure(NullResourceService.Instance);
            UnityEngine.Object.Destroy(m_prefab);
        }
        [UnityTest] public IEnumerator EventsWithoutFacade_AndUnscaledCloseComplete()
        {
            m_prefab.AddComponent<UIPageTransition>();
            var provider = new Provider { Prefab = m_prefab };
            UIRouter.Configure(new ResourceService(provider));
            int opened = 0, closed = 0;
            Action<UIPageHandle> onOpened = _ => opened++;
            Action<UIPageHandle> onClosed = _ => closed++;
            UIRouter.PageOpened += onOpened; UIRouter.PageClosed += onClosed;
            try
            {
                var loading = UIRouter.OpenAsync(new UIPageDef("Fade", null, new AssetKey("ui/fade")));
                yield return loading.AsEnumerator();
                var page = loading.GetResult();
                Assert.AreEqual(page, UIRouter.TopPage); Assert.AreEqual(1, opened);
                Time.timeScale = 0;
                var closing = UIRouter.CloseAsync(page);
                Assert.IsFalse(closing.IsDone);
                yield return closing.AsEnumerator(); closing.GetResult();
                Assert.IsFalse(page.IsValid); Assert.AreEqual(1, closed);
                Assert.IsNull(UIRouter.TopPage); Assert.AreEqual(1, provider.Released);
            }
            finally { UIRouter.PageOpened -= onOpened; UIRouter.PageClosed -= onClosed; }
        }
        [UnityTest] public IEnumerator CancelClose_PreservesPage_ImmediateCloseCancelsTransition()
        {
            m_prefab.AddComponent<UIPageTransition>();
            UIRouter.Configure(new ResourceService(new Provider { Prefab = m_prefab }));
            var loading = UIRouter.OpenAsync(new UIPageDef("CancelFade", null, new AssetKey("ui/fade")));
            yield return loading.AsEnumerator(); var page = loading.GetResult();
            using (var cts = new CancellationTokenSource())
            {
                var closing = UIRouter.CloseAsync(page, cts.Token);
                yield return null; cts.Cancel();
                yield return closing.AsEnumerator();
                Assert.Throws<OperationCanceledException>(() => closing.GetResult());
                Assert.IsTrue(page.IsValid);
                Assert.AreEqual(1, page.Context.Instance.GetComponent<CanvasGroup>().alpha);
            }
            var final = UIRouter.CloseAsync(page);
            UIRouter.Close(page);
            yield return final.AsEnumerator();
            Assert.Throws<OperationCanceledException>(() => final.GetResult());
            Assert.IsFalse(page.IsValid);
        }
        [UnityTest] public IEnumerator MandatoryPage_RejectsBack()
        {
            m_prefab.AddComponent<UIPageFocus>().AllowBack = false;
            UIRouter.Configure(new ResourceService(new Provider { Prefab = m_prefab }));
            var loading = UIRouter.OpenAsync(new UIPageDef("Mandatory", null, new AssetKey("ui/mandatory")));
            yield return loading.AsEnumerator();
            Assert.IsFalse(UIRouter.Back());
            Assert.IsTrue(loading.GetResult().IsValid);
        }
    }
}
