using System.Collections.Generic;
using NUnit.Framework;
using Yato.Resources;

namespace Yato.Tests.EditMode
{
    public sealed class ManifestTests
    {
        private const string ValidJson = @"{
          ""schema"": 2,
          ""version"": ""1.0.0"",
          ""platform"": ""Windows"",
          ""bundles"": {
            ""ui/bag"": { ""hash"": ""h1"", ""crc"": 1, ""size"": 10, ""compression"": ""LZ4"", ""deps"": [""ui/common""], ""assets"": [""ui/bag/bagform""] },
            ""ui/common"": { ""hash"": ""h2"", ""crc"": 2, ""size"": 5, ""deps"": [], ""assets"": [""ui/common/btn_close""] }
          },
          ""assets"": { ""ui/bag/bagform"": ""ui/bag"", ""ui/common/btn_close"": ""ui/common"" }
        }";

        [Test]
        public void Parse_BuildsIndexAndDependencies()
        {
            ResourceManifest manifest = ResourceManifest.Parse(ValidJson);

            Assert.AreEqual(2, manifest.BundleCount);
            Assert.AreEqual(2, manifest.AssetCount);

            ResourceManifest.BundleInfo bundle;
            Assert.IsTrue(manifest.TryGetBundleOfAsset("UI/Bag/BagForm.prefab", out bundle));
            Assert.AreEqual("ui/bag", bundle.Name);

            List<ResourceManifest.BundleInfo> order = manifest.ResolveLoadOrder("ui/bag");
            Assert.AreEqual(2, order.Count);
            Assert.AreEqual("ui/common", order[0].Name, "依赖必须先于自身加载");
            Assert.AreEqual("ui/bag", order[1].Name);
        }

        [Test]
        public void Parse_RejectsWrongSchema()
        {
            string json = ValidJson.Replace("\"schema\": 2", "\"schema\": 99");
            Assert.Throws<ResourceException>(() => ResourceManifest.Parse(json));
        }

        [Test]
        public void Parse_RejectsAssetPointingToMissingBundle()
        {
            string json = ValidJson.Replace("\"ui/bag/bagform\": \"ui/bag\"", "\"ui/bag/bagform\": \"ui/missing\"");
            Assert.Throws<ResourceException>(() => ResourceManifest.Parse(json));
        }

        [Test]
        public void ResolveLoadOrder_DetectsCycle()
        {
            const string json = @"{
              ""schema"": 2, ""version"": ""1"", ""platform"": ""Windows"",
              ""bundles"": {
                ""a"": { ""deps"": [""b""], ""assets"": [] },
                ""b"": { ""deps"": [""a""], ""assets"": [] }
              },
              ""assets"": {}
            }";

            ResourceManifest manifest = ResourceManifest.Parse(json);
            Assert.Throws<ResourceException>(() => manifest.ResolveLoadOrder("a"));
        }
    }

    public sealed class BundleRefCounterTests
    {
        [Test]
        public void Release_ToZero_SchedulesUnloadOnce()
        {
            var unloaded = new List<string>();
            var counter = new BundleRefCounter(delegate(string name) { unloaded.Add(name); });
            counter.UnloadDelaySeconds = 0f;

            counter.AddRef("ui/bag");
            counter.AddRef("ui/bag");
            Assert.AreEqual(2, counter.Get("ui/bag").RefCount);

            counter.Release("ui/bag");
            counter.Release("ui/bag");
            Assert.AreEqual(0, counter.Get("ui/bag").RefCount);

            // 重复 Release 不会重复入队
            counter.Release("ui/bag");
            Assert.AreEqual(1, counter.PendingCount, "延迟队列按名字去重");

            counter.FlushExpired();
            Assert.AreEqual(1, unloaded.Count);
            Assert.AreEqual("ui/bag", unloaded[0]);
        }

        [Test]
        public void AddRef_CancelsPendingUnload()
        {
            var counter = new BundleRefCounter(delegate(string name) { });
            counter.UnloadDelaySeconds = 100f;

            counter.AddRef("x");
            counter.Release("x");
            Assert.AreEqual(1, counter.PendingCount);

            counter.AddRef("x");
            Assert.AreEqual(0, counter.PendingCount);
        }
    }
}
