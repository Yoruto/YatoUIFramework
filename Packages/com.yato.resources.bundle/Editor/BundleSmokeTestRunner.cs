using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Yato.Resources;

namespace Yato.Editor
{
    /// <summary>Bundle-only contract smoke checks, available with the optional Bundle module.</summary>
    public static class BundleSmokeTestRunner
    {
        [MenuItem("Yato/Debug/Run Bundle Smoke Test", false, 601)]
        public static void Run()
        {
            ValidateForBatch();
            Debug.Log("[Yato.Bundle] Manifest, dependency cycle and reference counter smoke checks passed.");
        }

        public static void ValidateForBatch()
        {
            Check("Manifest", VerifyManifest);
            Check("Dependency cycles", VerifyCycleDetection);
            Check("Reference counter", VerifyRefCounter);
        }

        private static void Check(string name, Func<string> verify)
        {
            string error = verify();
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(name + ": " + error);
        }

        private static string VerifyManifest()
        {
            const string json = @"{
              ""schema"": 2, ""version"": ""1.0.0"", ""platform"": ""Windows"",
              ""bundles"": {
                ""ui/bag"": { ""crc"": 1, ""size"": 10, ""deps"": [""ui/common""], ""assets"": [""ui/bag/bagform""] },
                ""ui/common"": { ""crc"": 2, ""size"": 5, ""deps"": [], ""assets"": [""ui/common/btn""] }
              },
              ""assets"": { ""ui/bag/bagform"": ""ui/bag"", ""ui/common/btn"": ""ui/common"" }
            }";

            ResourceManifest manifest = ResourceManifest.Parse(json);
            if (manifest.BundleCount != 2 || manifest.AssetCount != 2)
            {
                return "索引数量错误";
            }

            List<ResourceManifest.BundleInfo> order = manifest.ResolveLoadOrder("ui/bag");
            if (order.Count != 2 || order[0].Name != "ui/common" || order[1].Name != "ui/bag")
            {
                return "依赖顺序错误（依赖必须在自身之前）";
            }

            ResourceManifest.BundleInfo bundle;
            if (!manifest.TryGetBundleOfAsset("UI/Bag/BagForm", out bundle) || bundle.Name != "ui/bag")
            {
                return "asset → bundle 索引错误";
            }

            return null;
        }

        private static string VerifyCycleDetection()
        {
            const string json = @"{
              ""schema"": 2, ""version"": ""1"", ""platform"": ""Windows"",
              ""bundles"": {
                ""a"": { ""deps"": [""b""], ""assets"": [] },
                ""b"": { ""deps"": [""a""], ""assets"": [] }
              },
              ""assets"": {}
            }";

            try
            {
                ResourceManifest.Parse(json).ResolveLoadOrder("a");
                return "循环依赖未抛出异常";
            }
            catch (ResourceException)
            {
                return null;
            }
        }

        private static string VerifyRefCounter()
        {
            var unloaded = new List<string>();
            var counter = new BundleRefCounter(delegate(string name) { unloaded.Add(name); });
            counter.UnloadDelaySeconds = 0f;

            counter.AddRef("ui/bag");
            counter.AddRef("ui/bag");
            counter.Release("ui/bag");
            counter.Release("ui/bag");
            counter.Release("ui/bag");

            if (counter.PendingCount != 1)
            {
                return "延迟卸载队列未按名字去重，PendingCount=" + counter.PendingCount;
            }

            counter.FlushExpired();
            if (unloaded.Count != 1 || unloaded[0] != "ui/bag")
            {
                return "到期未卸载或重复卸载";
            }

            return null;
        }

    }
}
