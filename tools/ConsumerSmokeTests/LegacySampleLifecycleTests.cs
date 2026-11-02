using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;
using Yato.App;
using Yato.FrameworkDemo;

namespace Yato.ConsumerTests
{
    public sealed class LegacySampleLifecycleTests
    {
        [UnityTest] public IEnumerator RestartLegacyService_DoesNotAccumulateSceneSubscriptions()
        {
            GameBootstrap.Shutdown();
            GameBootstrap.Initialize();
            var scenes = GameBootstrap.Scene;
            var started = scenes.GetType().GetField("LoadStarted", BindingFlags.Instance | BindingFlags.NonPublic);
            var completed = scenes.GetType().GetField("LoadCompleted", BindingFlags.Instance | BindingFlags.NonPublic);
            int beforeStarted = Count(started, scenes);
            int beforeCompleted = Count(completed, scenes);
            var service = new DemoAppService();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    service.Initialize();
                    service.Initialize();
                    Assert.AreEqual(beforeStarted + 1, Count(started, scenes));
                    Assert.AreEqual(beforeCompleted + 1, Count(completed, scenes));
                    service.Shutdown();
                    service.Shutdown();
                    Assert.AreEqual(beforeStarted, Count(started, scenes));
                    Assert.AreEqual(beforeCompleted, Count(completed, scenes));
                }
            }
            finally { service.Shutdown(); GameBootstrap.Shutdown(); }
            yield return null;
        }
        private static int Count(FieldInfo field, object target)
        {
            return ((System.Delegate)field.GetValue(target))?.GetInvocationList().Length ?? 0;
        }
    }
}
