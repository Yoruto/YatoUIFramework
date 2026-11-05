using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Yato.UI;

namespace Yato.Tests.PlayMode
{
    public sealed class UIListPoolTests
    {
        [Test]
        public void GetBeyondActiveCountExpandsAnActivePrefixAndReusesCells()
        {
            var root = new GameObject("UIListPool regression");
            var cells = new List<RectTransform>();
            var pool = new UIListPool<RectTransform>(root.transform, () =>
            {
                var cell = new GameObject("Cell", typeof(RectTransform)).GetComponent<RectTransform>();
                cells.Add(cell); return cell;
            }, prewarm: 2);
            try
            {
                RectTransform last = pool.Get(4);
                Assert.AreEqual(5, pool.ActiveCount);
                Assert.AreEqual(5, pool.Capacity);
                var visited = new List<RectTransform>();
                pool.ForEachActive((cell, index) =>
                {
                    Assert.IsTrue(cell.gameObject.activeSelf);
                    Assert.AreSame(cells[index], cell);
                    visited.Add(cell);
                });
                CollectionAssert.AreEqual(cells, visited);
                Assert.AreSame(cells[4], last);

                pool.SetCount(1);
                Assert.IsFalse(last.gameObject.activeSelf);
                Assert.AreSame(last, pool.Get(4));
                Assert.AreEqual(5, pool.ActiveCount);
                Assert.AreEqual(5, cells.Count, "重新扩展必须复用原有 Cell");
                pool.ReleaseAll();
                Assert.AreEqual(0, pool.ActiveCount);
                foreach (var cell in cells) Assert.IsFalse(cell.gameObject.activeSelf);
                int visits = 0;
                pool.ForEachActive((_, __) => visits++);
                Assert.AreEqual(0, visits);
            }
            finally { pool.Dispose(); Object.Destroy(root); }
        }
    }
}
