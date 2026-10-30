using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.Resources;
using Yato.UI;

namespace Yato.Tests.PlayMode
{
    /// <summary>
    /// 自研 AB 管线冒烟（P7 验证清单的测试化）：
    /// 前置是已通过 <c>Yato/Build/Bundles (Windows)</c>（或 CI 的
    /// <c>BuildPipelineEntry.BuildBundlesWindows</c>）产出 AB 并拷贝到 StreamingAssets。
    /// manifest 缺失时整组用例 Skip，不误报失败。
    /// </summary>
    public sealed class BundleModeSmokeTests
    {
        private static readonly AssetKey ExampleKey = new AssetKey("ui/example/example");

        private IResourceService m_service;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            ResourceServiceFactory.ForceBundleMode = true;

            if (!System.IO.File.Exists(RuntimePaths.GetManifestPath(false)))
            {
                ResourceServiceFactory.ForceBundleMode = false;
                Assert.Ignore("未找到 AB manifest，请先打包：Yato/Build/Bundles (Windows)");
                yield break;
            }

            m_service = ResourceServiceFactory.Create();
            Assert.IsInstanceOf<BundleProvider>(((ResourceService)m_service).Provider, "应选中 BundleProvider");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UIRouter.CloseAll();

            // 完整卸载本测试 service 持有的 bundle：
            // 否则 5s 延迟卸载窗口内，下一个测试的新 service 对同一文件 LoadFromFile 会报
            // "another AssetBundle with the same files is already loaded"（已知限制：同进程多 service 不共享 AB 缓存）
            if (m_service != null)
            {
                var provider = ((ResourceService)m_service).Provider as BundleProvider;
                if (provider != null)
                {
                    provider.Shutdown();
                }
            }

            ResourceServiceFactory.ForceBundleMode = false;
            m_service = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Load_Prefab_ByKey()
        {
            IAwaitable<IAssetHandle<GameObject>> loading = m_service.LoadAsync<GameObject>(ExampleKey);
            yield return loading.AsEnumerator();

            IAssetHandle<GameObject> handle = loading.GetResult();
            Assert.IsNotNull(handle, "句柄不应为空");
            Assert.IsNotNull(handle.Asset, "预制体不应为空");
            Assert.AreEqual(1, handle.RefCount);

            handle.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Instantiate_Release_DestroysInstance()
        {
            IAwaitable<IAssetHandle<GameObject>> loading = m_service.InstantiateAsync(ExampleKey, null);
            yield return loading.AsEnumerator();

            IAssetHandle<GameObject> handle = loading.GetResult();
            GameObject instance = handle.Asset;
            Assert.IsNotNull(instance, "实例不应为空");

            handle.Dispose();
            yield return null; // Destroy 是帧末生效

            Assert.IsTrue(instance == null, "释放句柄后实例应被销毁");
        }

        [UnityTest]
        public IEnumerator Cycles100_LoadRelease_Stable()
        {
            for (int i = 0; i < 100; i++)
            {
                IAwaitable<IAssetHandle<GameObject>> loading = m_service.LoadAsync<GameObject>(ExampleKey);
                yield return loading.AsEnumerator();

                IAssetHandle<GameObject> handle = loading.GetResult();
                Assert.IsNotNull(handle.Asset, "第 " + i + " 次加载结果为空");
                handle.Dispose();
            }

            var service = (ResourceService)m_service;
            Assert.AreEqual(0, service.ActiveAssetCount, "全部释放后活动资产数应为 0");
        }

        [UnityTest]
        public IEnumerator DelayedUnload_AfterWindow_ReleasesBundles()
        {
            IAwaitable<IAssetHandle<GameObject>> loading = m_service.LoadAsync<GameObject>(ExampleKey);
            yield return loading.AsEnumerator();
            loading.GetResult().Dispose();

            // 默认 5s 延迟卸载窗口；等窗口过期后手动触发清理
            yield return new WaitForSecondsRealtime(5.5f);
            m_service.ReleaseUnused();

            ResourceStats stats = m_service.Stats;
            Assert.AreEqual(0, stats.LoadedBundleCount, "窗口过期并清理后，不应残留已加载 bundle");
            Assert.AreEqual(0, stats.TotalBundleBytes, "不应残留 bundle 内存占用");
        }

        [UnityTest]
        public IEnumerator MissingKey_FailsClearly()
        {
            // ResourceService 失败会打一条 Error + 一条 Exception（UnityLogger.Error 的双输出），均为预期
            LogAssert.Expect(LogType.Error, new Regex("ResourceService"));
            LogAssert.Expect(LogType.Exception, new Regex("ResourceException"));

            var missing = new AssetKey("ui/none/nothere");
            IAwaitable<IAssetHandle<GameObject>> loading = m_service.LoadAsync<GameObject>(missing);
            yield return loading.AsEnumerator();

            Assert.Throws<ResourceException>(delegate { loading.GetResult(); });
            Assert.IsFalse(UIRouter.IsOpen("MissingKey"), "资源失败不应影响路由状态");
        }

        [UnityTest]
        public IEnumerator UIRouter_10OpenCloseCycles_WithMediator()
        {
            UIRouter.Configure(m_service, null);

            for (int i = 0; i < 10; i++)
            {
                var def = new UIPageDef(
                    "SmokePage", typeof(SmokeMediator), ExampleKey,
                    UILayer.Normal, cacheable: false, singleton: false);

                IAwaitable<UIPageHandle> opening = UIRouter.OpenAsync(def, i);
                yield return opening.AsEnumerator();

                UIPageHandle handle = opening.GetResult();
                Assert.IsTrue(handle.IsValid, "第 " + i + " 次打开句柄无效");
                Assert.IsTrue(UIRouter.IsOpen("SmokePage"), "第 " + i + " 次打开后应处于打开状态");

                SmokeView view = handle.Context.View as SmokeView;
                Assert.IsNotNull(view, "应由 Mediator 泛型推断补挂 View");
                Assert.AreEqual(i, view.LastArgs, "OnOpen 应收到参数");

                UIRouter.Close(handle);
                Assert.IsFalse(UIRouter.IsOpen("SmokePage"), "关闭后不应仍处于打开状态");
            }
        }

        private sealed class SmokeView : UIPageView
        {
            public int LastArgs = -1;

            public override void OnOpen(object args)
            {
                LastArgs = args is int ? (int)args : -1;
            }
        }

        private sealed class SmokeMediator : UIPageMediator<SmokeView>
        {
            public SmokeMediator(string mediatorName) : base(mediatorName)
            {
            }
        }
    }
}
