using NUnit.Framework;
using Yato.Resources;
using Yato.UI;

namespace Yato.Tests.EditMode
{
    /// <summary>
    /// 页面缓存回归测试（文档 §5.1 风险线索 B）：
    /// 同一个 pageId 不能被静默替换，否则旧实例与预制体句柄会永久泄漏。
    /// </summary>
    public sealed class UIPageCacheTests
    {
        private static UIPageContext CreateContext(string pageId)
        {
            var def = new UIPageDef(pageId, null, new AssetKey("ui/" + pageId.ToLowerInvariant()));
            return new UIPageContext { Def = def };
        }

        [Test]
        public void Add_SameId_ReturnsPreviousInstance()
        {
            var cache = new UIPageCache();
            UIPageContext first = CreateContext("Bag");
            UIPageContext second = CreateContext("Bag");

            Assert.IsNull(cache.Add("Bag", first));

            UIPageContext replaced = cache.Add("Bag", second);

            Assert.AreSame(first, replaced, "被替换的实例必须返回给调用方销毁，不能静默丢弃");
            Assert.AreEqual(1, cache.Count);

            UIPageContext current;
            Assert.IsTrue(cache.TryGet("Bag", out current));
            Assert.AreSame(second, current);
        }

        [Test]
        public void Add_SameInstanceTwice_DoesNotReturnItself()
        {
            var cache = new UIPageCache();
            UIPageContext context = CreateContext("Bag");

            cache.Add("Bag", context);
            UIPageContext replaced = cache.Add("Bag", context);

            Assert.IsNull(replaced, "同一实例重复入缓存不能被当成被淘汰实例而销毁");
        }

        [Test]
        public void Add_ExceedingCapacity_EvictsLeastRecentlyUsed()
        {
            var cache = new UIPageCache(2);
            cache.Add("A", CreateContext("A"));
            cache.Add("B", CreateContext("B"));

            UIPageContext evicted = cache.Add("C", CreateContext("C"));

            Assert.IsNotNull(evicted);
            Assert.AreEqual("A", evicted.PageId, "最久未使用的实例应被淘汰");
            Assert.IsFalse(cache.Contains("A"));
            Assert.IsTrue(cache.Contains("B"));
            Assert.IsTrue(cache.Contains("C"));
        }

        [Test]
        public void TryGet_RefreshesLruOrder()
        {
            var cache = new UIPageCache(2);
            cache.Add("A", CreateContext("A"));
            cache.Add("B", CreateContext("B"));

            UIPageContext unused;
            Assert.IsTrue(cache.TryGet("A", out unused));

            UIPageContext evicted = cache.Add("C", CreateContext("C"));

            Assert.AreEqual("B", evicted.PageId, "命中后 A 应变为最近使用");
        }

        [Test]
        public void Remove_TakesInstanceOutOfCache()
        {
            var cache = new UIPageCache();
            cache.Add("Bag", CreateContext("Bag"));

            UIPageContext removed;
            Assert.IsTrue(cache.Remove("Bag", out removed));
            Assert.IsNotNull(removed);
            Assert.IsFalse(cache.Contains("Bag"), "取出的实例不能再留在缓存中");
            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void Clear_ReturnsAllInstances()
        {
            var cache = new UIPageCache();
            cache.Add("A", CreateContext("A"));
            cache.Add("B", CreateContext("B"));

            var all = cache.Clear();

            Assert.AreEqual(2, all.Count);
            Assert.AreEqual(0, cache.Count);
        }
    }
}
