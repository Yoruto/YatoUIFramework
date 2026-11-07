using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;
using Yato.Core;
using Yato.Resources;
using Yato.UI;
using Debug = UnityEngine.Debug;

namespace Yato.Editor
{
    /// <summary>
    /// 一键冒烟：自动进入 Play，依次验证 Core / 资源契约 / UI 层的核心能力，
    /// 输出 PASS/FAIL 清单后退出 Play。菜单：Yato/Debug/Run Smoke Test。
    /// 只使用运行时 API，不依赖示例资源，因此任何工程都可直接跑。
    /// </summary>
    public static class SmokeTestRunner
    {
        private const string MenuPath = "Yato/Debug/Run Smoke Test";
        private const string PendingKey = "Yato.SmokeTest.Pending";
        private const string RunningKey = "Yato.SmokeTest.Running";

        private static int s_waitTicks;

        [MenuItem(MenuPath, false, 600)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Execute();
                return;
            }

            SessionState.SetBool(PendingKey, true);
            EditorApplication.isPlaying = true;
            Debug.Log("[SmokeTest] 正在进入 Play 模式…");
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
            {
                SessionState.SetBool(PendingKey, false);
                // 等两帧，确保 RuntimeHost.Bootstrap 与静态重置都已完成
                s_waitTicks = 2;
                EditorApplication.update -= WaitThenRun;
                EditorApplication.update += WaitThenRun;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= WaitThenRun;
            }
        }

        private static void WaitThenRun()
        {
            if (s_waitTicks-- > 0)
            {
                return;
            }

            EditorApplication.update -= WaitThenRun;
            Execute();
        }

        private static void Execute()
        {
            if (SessionState.GetBool(RunningKey, false))
            {
                return;
            }

            SessionState.SetBool(RunningKey, true);
            var results = new List<string>();
            int passed = 0;
            int failed = 0;
            var watch = Stopwatch.StartNew();

            Check(results, ref passed, ref failed, "AssetKey 规范化", VerifyAssetKey);
            Check(results, ref passed, ref failed, "EventBus 订阅/退订/异常隔离", VerifyEventBus);
            Check(results, ref passed, ref failed, "DisposableBag 逆序释放", VerifyDisposableBag);
            Check(results, ref passed, ref failed, "ServiceLocator 注册/解析", VerifyServiceLocator);
            Check(results, ref passed, ref failed, "UIRoot 五层 Canvas", VerifyUIRoot);
            Check(results, ref passed, ref failed, "UIMask / UILoading 显隐", VerifyOverlays);
            Check(results, ref passed, ref failed, "UIListPool 复用", VerifyListPool);

            watch.Stop();

            var builder = new StringBuilder();
            builder.AppendFormat("[SmokeTest] 完成：通过 {0}，失败 {1}，耗时 {2}ms\n", passed, failed, watch.ElapsedMilliseconds);
            for (int i = 0; i < results.Count; i++)
            {
                builder.Append("  ").Append(results[i]).Append('\n');
            }

            if (failed > 0)
            {
                Debug.LogError(builder.ToString());
            }
            else
            {
                Debug.Log(builder.ToString());
            }

            SessionState.SetBool(RunningKey, false);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(failed > 0 ? 1 : 0);
            }
            else if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }

        #region 检查项

        private static string VerifyAssetKey()
        {
            var key = new AssetKey("UI\\Bag\\BagForm.prefab");
            if (key.Value != "ui/bag/bagform")
            {
                return "规范化结果错误：" + key.Value;
            }

            if (new AssetKey("  ").IsValid)
            {
                return "空 key 不应有效";
            }

            return null;
        }

        private static string VerifyEventBus()
        {
            var bus = new EventBus();
            int received = 0;
            int afterThrow = 0;

            bus.Subscribe<int>(delegate(int value) { received = value; });
            bus.Subscribe<int>(delegate(int value) { throw new InvalidOperationException("冒烟测试：故意抛错"); });
            bus.Subscribe<int>(delegate(int value) { afterThrow = value; });

            bus.Publish(7);

            if (received != 7 || afterThrow != 7)
            {
                return string.Format("异常中断了派发（received={0}, after={1}）", received, afterThrow);
            }

            bus.Clear();
            bus.Publish(9);
            if (received != 7)
            {
                return "Clear 后仍然收到事件";
            }

            return null;
        }

        private static string VerifyDisposableBag()
        {
            var order = new List<int>();
            var bag = new DisposableBag();
            bag.Add(delegate { order.Add(1); });
            bag.Add(delegate { order.Add(2); });
            bag.Dispose();
            bag.Dispose();

            if (order.Count != 2 || order[0] != 2 || order[1] != 1)
            {
                return "释放顺序不是逆序或重复释放：" + string.Join(",", order);
            }

            return null;
        }

        private static string VerifyServiceLocator()
        {
            var locator = new ServiceLocator();
            var logger = new UnityLogger("SmokeTest");
            locator.Register<Yato.Core.ILogger>(logger);

            Yato.Core.ILogger resolved;
            if (!locator.TryResolve(out resolved) || !ReferenceEquals(resolved, logger))
            {
                return "注册后无法解析";
            }

            locator.Unregister<Yato.Core.ILogger>();
            Yato.Core.ILogger missing;
            if (locator.TryResolve(out missing))
            {
                return "注销后仍能解析";
            }

            return null;
        }

        private static string VerifyUIRoot()
        {
            UIRoot root = UIRoot.Instance;
            if (root == null)
            {
                return "UIRoot 未创建";
            }

            for (int i = 0; i <= (int)UILayer.System; i++)
            {
                UILayerRoot layer = root.GetLayerRoot((UILayer)i);
                if (layer == null || layer.Canvas == null)
                {
                    return "缺少层级或 Canvas：" + (UILayer)i;
                }
            }

            return null;
        }

        private static string VerifyOverlays()
        {
            UIMask mask = UIMask.Show(null);
            if (mask == null || !mask.IsVisible)
            {
                return "UIMask 未能显示";
            }

            UIMask.Hide();
            if (mask.IsVisible)
            {
                return "UIMask 未能隐藏";
            }

            UILoading.Show("冒烟测试", 0.5f);
            if (!UILoading.IsVisible)
            {
                return "UILoading 未能显示";
            }

            UILoading.SetProgress(0.8f);
            UILoading.Hide();
            if (UILoading.IsVisible)
            {
                return "UILoading 未能隐藏";
            }

            return null;
        }

        private static string VerifyListPool()
        {
            var parent = new GameObject("SmokeTest.PoolRoot");
            var pool = new UIListPool<RectTransform>(
                parent.transform,
                delegate
                {
                    var go = new GameObject("Cell", typeof(RectTransform));
                    return go.GetComponent<RectTransform>();
                },
                prewarm: 2);

            pool.SetCount(4);
            if (pool.Capacity != 4 || pool.ActiveCount != 4)
            {
                return string.Format("扩容失败 capacity={0} active={1}", pool.Capacity, pool.ActiveCount);
            }

            pool.SetCount(1);
            if (pool.ActiveCount != 1 || pool.Capacity != 4)
            {
                return "缩容后实例未被复用";
            }

            pool.Dispose();
            UnityEngine.Object.Destroy(parent);
            return null;
        }

        #endregion

        private static void Check(List<string> results, ref int passed, ref int failed, string name, Func<string> verify)
        {
            try
            {
                string error = verify();
                if (string.IsNullOrEmpty(error))
                {
                    passed++;
                    results.Add("[PASS] " + name);
                }
                else
                {
                    failed++;
                    results.Add("[FAIL] " + name + " → " + error);
                }
            }
            catch (Exception e)
            {
                failed++;
                results.Add("[FAIL] " + name + " → 异常：" + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
