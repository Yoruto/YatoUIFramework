using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.Core;
using Yato.Resources;
using Yato.UI;

namespace Yato.Tests.PlayMode
{
    public sealed class RouterLifecycleTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            UIRouter.CloseAll();
            UIRouter.Configure(NullResourceService.Instance, null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UIRoot_CreatesAllLayers()
        {
            UIRoot root = UIRoot.Instance;
            Assert.IsNotNull(root);

            for (int i = 0; i <= (int)UILayer.System; i++)
            {
                UILayerRoot layer = root.GetLayerRoot((UILayer)i);
                Assert.IsNotNull(layer, "缺少层级：" + (UILayer)i);
                Assert.IsNotNull(layer.Canvas);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator OpenAsync_WithoutResourceService_FailsClearly()
        {
            // 打开失败路径：工厂与路由各打一条 Error + 一条 Exception（UnityLogger.Error 的双输出），均为预期
            LogAssert.Expect(LogType.Error, new Regex("UIPageFactory"));
            LogAssert.Expect(LogType.Exception, new Regex("ResourceException"));
            LogAssert.Expect(LogType.Error, new Regex("UIRouter"));
            LogAssert.Expect(LogType.Exception, new Regex("ResourceException"));

            var def = new UIPageDef("NoRes", null, new AssetKey("ui/test/none"));
            IAwaitable<UIPageHandle> opening = UIRouter.OpenAsync(def);
            while (!opening.IsDone)
            {
                yield return null;
            }

            Assert.Throws<ResourceException>(delegate { opening.GetResult(); });
            Assert.IsFalse(UIRouter.IsOpen("NoRes"), "失败不应留下打开状态");
        }

        [UnityTest]
        public IEnumerator CancelOpen_DoesNotLeaveOrphan()
        {
            // 同上：四条预期日志（2 Error + 2 Exception）
            LogAssert.Expect(LogType.Error, new Regex("UIPageFactory"));
            LogAssert.Expect(LogType.Exception, new Regex("ResourceException"));
            LogAssert.Expect(LogType.Error, new Regex("UIRouter"));
            LogAssert.Expect(LogType.Exception, new Regex("ResourceException"));

            // NullResourceService 同步失败，OpenAsync 内联完成、无取消窗口；
            // 用"下一帧才失败"的存根制造真实加载中状态
            UIRouter.Configure(new OneFrameResourceService(), null);

            var def = new UIPageDef("CancelTest", null, new AssetKey("ui/test/none"));
            IAwaitable<UIPageHandle> opening = UIRouter.OpenAsync(def);

            Assert.IsTrue(UIRouter.CancelOpen("CancelTest"), "加载进行中应可取消");

            while (!opening.IsDone)
            {
                yield return null;
            }

            bool failed = false;
            try
            {
                opening.GetResult();
            }
            catch (ResourceException)
            {
                failed = true;
            }
            catch (System.OperationCanceledException)
            {
                failed = true;
            }

            Assert.IsTrue(failed, "取消后应返回异常而不是句柄");
            Assert.IsFalse(UIRouter.IsOpen("CancelTest"));
        }

        /// <summary>下一帧才失败的存根服务：用于制造"加载中"状态（供 CancelOpen 测试）。</summary>
        private sealed class OneFrameResourceService : IResourceService
        {
            public ResourceStats Stats
            {
                get { return ResourceStats.Empty; }
            }

            public IAssetHandle<T> Load<T>(AssetKey key) where T : UnityEngine.Object
            {
                throw new ResourceException(key.ToString(), null, ResourceStage.ProviderSelect, "存根不支持同步加载");
            }

            public IAwaitable<IAssetHandle<T>> LoadAsync<T>(AssetKey key, CancellationToken ct = default(CancellationToken))
                where T : UnityEngine.Object
            {
                // Unity 6 引入同名 UnityEngine.AwaitableCompletionSource，必须全限定
                var source = new Yato.Resources.AwaitableCompletionSource<IAssetHandle<T>>();
                RuntimeHost.RunCoroutine(FailNextFrame<T>(source, key));
                return source;
            }

            public IAwaitable<IAssetHandle<GameObject>> InstantiateAsync(
                AssetKey key,
                Transform parent,
                CancellationToken ct = default(CancellationToken))
            {
                throw new ResourceException(key.ToString(), null, ResourceStage.ProviderSelect, "存根不支持实例化");
            }

            public IAwaitable PreloadAsync(
                IReadOnlyList<AssetKey> keys,
                IProgress<float> progress = null,
                CancellationToken ct = default(CancellationToken))
            {
                throw new ResourceException(null, null, ResourceStage.ProviderSelect, "存根不支持预加载");
            }

            public void Release(IAssetHandle handle)
            {
            }

            public void ReleaseUnused()
            {
            }

            private static IEnumerator FailNextFrame<T>(
                Yato.Resources.AwaitableCompletionSource<IAssetHandle<T>> source,
                AssetKey key)
                where T : UnityEngine.Object
            {
                yield return null;
                source.SetException(new ResourceException(
                    key.ToString(), null, ResourceStage.ProviderSelect, "存根：下一帧加载失败"));
            }
        }

        [UnityTest]
        public IEnumerator Back_WithoutPages_ReturnsFalse()
        {
            yield return null;
            Assert.IsFalse(UIRouter.Back());
        }
    }
}
