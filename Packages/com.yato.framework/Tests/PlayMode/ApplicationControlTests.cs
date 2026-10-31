using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.App;

namespace Yato.Tests.PlayMode
{
    public sealed class ApplicationControlTests
    {
        [Test] public void NestedPause_RestoresOriginalScaleAndIsIdempotent()
        {
            float original = Time.timeScale;
            try
            {
                Time.timeScale = 0.75f;
                using (var pause = new PauseService())
                {
                    var first = pause.Acquire("menu"); var second = pause.Acquire("dialog");
                    Assert.AreEqual(0, Time.timeScale);
                    first.Dispose(); first.Dispose();
                    Assert.IsTrue(pause.IsPaused);
                    second.Dispose();
                    Assert.AreEqual(0.75f, Time.timeScale);
                    var held = pause.Acquire("held"); pause.Dispose(); held.Dispose();
                    Assert.AreEqual(0.75f, Time.timeScale);
                }
            }
            finally { Time.timeScale = original; }
        }
        [UnityTest] public IEnumerator ExplicitExit_WaitsForStepsAndRejectsBusy()
        {
            var waiting = new TaskCompletionSource<bool>();
            int exits = 0;
            using (var exit = new ApplicationExitCoordinator(() => exits++))
            {
                exit.Register(_ => waiting.Task);
                var operation = exit.RequestExitAsync();
                Assert.IsFalse(operation.IsCompleted);
                Assert.IsFalse(exit.RequestExitAsync().Result);
                waiting.SetResult(true);
                while (!operation.IsCompleted) yield return null;
                Assert.IsTrue(operation.GetAwaiter().GetResult());
                Assert.AreEqual(1, exits);
            }
        }
        [Test] public void FocusPause_DoesNotReleaseOtherPauseReasons()
        {
            float scale = Time.timeScale;
            var host = Yato.Core.RuntimeHost.Instance.gameObject;
            bool focused = ApplicationLifecycle.IsFocused;
            try
            {
                host.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
                using (var pause = new PauseService(true))
                {
                    var menu = pause.Acquire("menu");
                    host.SendMessage("OnApplicationFocus", false, SendMessageOptions.DontRequireReceiver);
                    Assert.AreEqual(2, pause.PauseCount);
                    host.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
                    Assert.AreEqual(1, pause.PauseCount);
                    menu.Dispose();
                }
            }
            finally { host.SendMessage("OnApplicationFocus", focused, SendMessageOptions.DontRequireReceiver); Time.timeScale = scale; }
        }

        [UnityTest] public IEnumerator FailedExit_DoesNotRequestQuit()
        {
            int exits = 0;
            using (var exit = new ApplicationExitCoordinator(() => exits++))
            {
                exit.Register(_ => Task.FromException(new InvalidOperationException("save failed")));
                var operation = exit.RequestExitAsync();
                while (!operation.IsCompleted) yield return null;
                Assert.Throws<InvalidOperationException>(() => operation.GetAwaiter().GetResult());
                Assert.AreEqual(0, exits); Assert.IsFalse(exit.IsBusy);
            }
        }
    }
}
