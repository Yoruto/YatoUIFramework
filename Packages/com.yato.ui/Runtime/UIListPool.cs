using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yato.UI
{
    /// <summary>
    /// 列表 Cell 池（T-P3-05）：刷新只切换激活状态，不再产生 Instantiate/Destroy 峰值。
    /// 用法：
    /// <code>
    /// pool = new UIListPool&lt;ItemCell&gt;(content, () =&gt; cellPrefabInstance, prewarm: 10);
    /// pool.SetCount(data.Count);
    /// for (int i = 0; i &lt; data.Count; i++) pool.Get(i).Refresh(data[i]);
    /// </code>
    /// </summary>
    public sealed class UIListPool<T> where T : Component
    {
        private readonly Transform m_content;
        private readonly Func<T> m_factory;
        private readonly List<T> m_items = new List<T>();

        private int m_activeCount;

        public UIListPool(Transform content, Func<T> factory, int prewarm = 0)
        {
            if (content == null)
            {
                throw new ArgumentNullException("content");
            }

            if (factory == null)
            {
                throw new ArgumentNullException("factory");
            }

            m_content = content;
            m_factory = factory;

            if (prewarm > 0)
            {
                EnsureCapacity(prewarm);
            }
        }

        public int ActiveCount
        {
            get { return m_activeCount; }
        }

        /// <summary>池内实例总数（激活 + 复用中）。</summary>
        public int Capacity
        {
            get { return m_items.Count; }
        }

        /// <summary>取第 index 个 Cell（自动扩容并激活）。</summary>
        public T Get(int index)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException("index");
            }

            EnsureCapacity(index + 1);

            T item = m_items[index];
            if (!item.gameObject.activeSelf)
            {
                item.gameObject.SetActive(true);
            }

            return item;
        }

        /// <summary>只保留前 count 个激活，其余隐藏复用。</summary>
        public void SetCount(int count)
        {
            if (count < 0)
            {
                count = 0;
            }

            EnsureCapacity(count);

            for (int i = 0; i < m_items.Count; i++)
            {
                bool active = i < count;
                if (m_items[i].gameObject.activeSelf != active)
                {
                    m_items[i].gameObject.SetActive(active);
                }
            }

            m_activeCount = count;
        }

        /// <summary>遍历当前激活的 Cell。</summary>
        public void ForEachActive(Action<T, int> action)
        {
            if (action == null)
            {
                return;
            }

            for (int i = 0; i < m_activeCount && i < m_items.Count; i++)
            {
                action(m_items[i], i);
            }
        }

        /// <summary>全部隐藏（保留实例）。</summary>
        public void ReleaseAll()
        {
            SetCount(0);
        }

        /// <summary>销毁全部实例。</summary>
        public void Dispose()
        {
            for (int i = 0; i < m_items.Count; i++)
            {
                if (m_items[i] != null)
                {
                    UnityEngine.Object.Destroy(m_items[i].gameObject);
                }
            }

            m_items.Clear();
            m_activeCount = 0;
        }

        private void EnsureCapacity(int count)
        {
            while (m_items.Count < count)
            {
                T item = m_factory();
                if (item == null)
                {
                    Yato.Core.Log.Error("UIListPool", string.Format("Cell 工厂返回 null（{0}）", typeof(T).Name));
                    break;
                }

                item.transform.SetParent(m_content, false);
                item.gameObject.SetActive(false);
                m_items.Add(item);
            }
        }
    }
}
