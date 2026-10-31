using System;
using NUnit.Framework;
using UnityEngine;

namespace Yato.Config.Tests
{
    [Serializable] public sealed class Row : ConfigRecord
    {
        public int Value;
        public string Reference;
        public override void Validate(ConfigValidationContext context)
        {
            if (Value < 0) context.Report("Value must be nonnegative");
            if (!string.IsNullOrEmpty(Reference)) context.Require<Row>(Reference);
        }
    }
    public sealed class Table : ConfigTable<Row> { }
    public sealed class ConfigServiceTests
    {
        private ConfigCatalog m_catalog; private Table m_table;
        [SetUp] public void Setup() { m_catalog = ScriptableObject.CreateInstance<ConfigCatalog>(); m_table = ScriptableObject.CreateInstance<Table>(); m_catalog.Tables = new ConfigTableBase[] { m_table }; }
        [TearDown] public void Teardown() { UnityEngine.Object.DestroyImmediate(m_catalog); UnityEngine.Object.DestroyImmediate(m_table); }
        [Test] public void RuntimeRows_AreDetachedFromAsset()
        {
            m_table.Rows = new[] { new Row { Id = "a", Value = 3 } };
            using (var service = new ConfigService(m_catalog))
            {
                service.Get<Row>("a").Value = 42;
                Assert.AreEqual(3, m_table.Rows[0].Value);
                Assert.IsFalse(service.TryGet<Row>("missing", out _));
            }
        }
        [Test] public void DuplicateAndEmptyIds_AreReported()
        {
            m_table.Rows = new[] { new Row { Id = "a" }, new Row { Id = "a" }, new Row() };
            Assert.AreEqual(2, m_catalog.Validate().Count);
        }
        [Test] public void InvalidValuesAndCrossTableReferences_AreReported()
        {
            m_table.Rows = new[] { new Row { Id = "a", Value = -1, Reference = "missing" } };
            var error = Assert.Throws<ConfigValidationException>(() => new ConfigService(m_catalog));
            Assert.AreEqual(2, error.Errors.Count);
        }
    }
}
