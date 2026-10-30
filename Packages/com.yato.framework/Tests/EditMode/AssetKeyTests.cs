using NUnit.Framework;
using Yato.Resources;

namespace Yato.Tests.EditMode
{
    public sealed class AssetKeyTests
    {
        [Test]
        public void Normalize_LowercasesAndStripsExtension()
        {
            var key = new AssetKey("UI\\Bag\\BagForm.prefab");
            Assert.AreEqual("ui/bag/bagform", key.Value);
            Assert.IsTrue(key.IsValid);
        }

        [Test]
        public void Normalize_CollapsesSlashesAndTrims()
        {
            var key = new AssetKey("  //ui//common//btn_close.png//  ");
            Assert.AreEqual("ui/common/btn_close", key.Value);
        }

        [Test]
        public void Normalize_EmptyBecomesInvalid()
        {
            Assert.IsFalse(new AssetKey(null).IsValid);
            Assert.IsFalse(new AssetKey("   ").IsValid);
        }

        [Test]
        public void Equality_IsValueBased()
        {
            Assert.AreEqual(new AssetKey("ui/a"), new AssetKey("UI/A.prefab"));
            Assert.IsTrue(new AssetKey("ui/a") == new AssetKey("ui/a"));
            Assert.IsTrue(new AssetKey("ui/a") != new AssetKey("ui/b"));
        }
    }
}
