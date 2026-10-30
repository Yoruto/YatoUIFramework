using System.Collections.Generic;

namespace Yato.UI
{
    /// <summary>
    /// 页面实例缓存（LRU）。只缓存 <see cref="UIPageDef.Cacheable"/> 的页面，
    /// 命中时直接复用实例，不再重新实例化（T-P3-04 的基础设施）。
    /// </summary>
    public sealed class UIPageCache
    {
        private readonly Dictionary<string, UIPageContext> m_map =
            new Dictionary<string, UIPageContext>(System.StringComparer.Ordinal);

        /// <summary>最近使用在前。</summary>
        private readonly LinkedList<string> m_order = new LinkedList<string>();

        private readonly int m_capacity;

        public UIPageCache(int capacity = 8)
        {
            m_capacity = capacity < 1 ? 1 : capacity;
        }

        public int Capacity
        {
            get { return m_capacity; }
        }

        public int Count
        {
            get { return m_map.Count; }
        }

        public bool Contains(string pageId)
        {
            return !string.IsNullOrEmpty(pageId) && m_map.ContainsKey(pageId);
        }

        /// <summary>命中则取出并刷新为最近使用。</summary>
        public bool TryGet(string pageId, out UIPageContext context)
        {
            if (string.IsNullOrEmpty(pageId) || !m_map.TryGetValue(pageId, out context))
            {
                context = null;
                return false;
            }

            m_order.Remove(pageId);
            m_order.AddFirst(pageId);
            return true;
        }

        /// <summary>
        /// 加入缓存；超出容量时淘汰最久未使用的实例并返回，由调用方负责销毁与释放。
        /// </summary>
        public UIPageContext Add(string pageId, UIPageContext context)
        {
            if (string.IsNullOrEmpty(pageId) || context == null)
            {
                return null;
            }

            if (m_map.ContainsKey(pageId))
            {
                m_order.Remove(pageId);
                m_order.AddFirst(pageId);
                m_map[pageId] = context;
                return null;
            }

            m_map[pageId] = context;
            m_order.AddFirst(pageId);

            if (m_map.Count <= m_capacity)
            {
                return null;
            }

            string evictedId = m_order.Last.Value;
            m_order.RemoveLast();
            UIPageContext evicted;
            m_map.TryGetValue(evictedId, out evicted);
            m_map.Remove(evictedId);
            return evicted;
        }

        public bool Remove(string pageId, out UIPageContext context)
        {
            context = null;
            if (string.IsNullOrEmpty(pageId) || !m_map.TryGetValue(pageId, out context))
            {
                return false;
            }

            m_map.Remove(pageId);
            m_order.Remove(pageId);
            return true;
        }

        /// <summary>清空并返回全部实例，由调用方销毁。</summary>
        public List<UIPageContext> Clear()
        {
            var all = new List<UIPageContext>(m_map.Values);
            m_map.Clear();
            m_order.Clear();
            return all;
        }

        /// <summary>缓存中的页面 id（诊断用）。</summary>
        public List<string> GetPageIds()
        {
            return new List<string>(m_order);
        }
    }
}
