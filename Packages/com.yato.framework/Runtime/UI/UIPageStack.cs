using System;
using System.Collections.Generic;

namespace Yato.UI
{
    /// <summary>
    /// 按层级维护的页面栈：负责 OnCover / OnReveal 的时机，以及返回键弹栈（文档 7.1）。
    /// </summary>
    public sealed class UIPageStack
    {
        private readonly Dictionary<UILayer, List<UIPageHandle>> m_layers =
            new Dictionary<UILayer, List<UIPageHandle>>();

        /// <summary>入栈；若该层已有页面，对旧栈顶发 OnCover。</summary>
        public void Push(UIPageHandle handle)
        {
            if (handle == null || handle.Context == null || handle.Context.Def == null)
            {
                return;
            }

            List<UIPageHandle> stack = GetStack(handle.Context.Layer);
            UIPageHandle covered = stack.Count > 0 ? stack[stack.Count - 1] : null;
            stack.Add(handle);

            if (covered != null && covered.Context != null && covered.Context.Page != null)
            {
                SafeInvoke(covered, delegate { covered.Context.Page.OnCover(); });
            }
        }

        /// <summary>出栈；若该层仍有页面，对新栈顶发 OnReveal。</summary>
        public bool Pop(UIPageHandle handle)
        {
            if (handle == null || handle.Context == null || handle.Context.Def == null)
            {
                return false;
            }

            List<UIPageHandle> stack = GetStack(handle.Context.Layer);
            if (!stack.Remove(handle))
            {
                return false;
            }

            UIPageHandle revealed = stack.Count > 0 ? stack[stack.Count - 1] : null;
            if (revealed != null && revealed.Context != null && revealed.Context.Page != null)
            {
                SafeInvoke(revealed, delegate { revealed.Context.Page.OnReveal(); });
            }

            return true;
        }

        public UIPageHandle Peek(UILayer layer)
        {
            List<UIPageHandle> stack = GetStack(layer);
            return stack.Count > 0 ? stack[stack.Count - 1] : null;
        }

        /// <summary>按页面 id 查找（只返回仍未关闭的句柄）。</summary>
        public UIPageHandle Find(string pageId)
        {
            if (string.IsNullOrEmpty(pageId))
            {
                return null;
            }

            foreach (KeyValuePair<UILayer, List<UIPageHandle>> pair in m_layers)
            {
                List<UIPageHandle> stack = pair.Value;
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    if (stack[i] != null && stack[i].PageId == pageId)
                    {
                        return stack[i];
                    }
                }
            }

            return null;
        }

        public bool Contains(string pageId)
        {
            return Find(pageId) != null;
        }

        /// <summary>
        /// 返回键：从高层到低层找到最上面的可返回页面（跳过 Background 与 System）。
        /// 返回 null 表示没有可关闭的页面。
        /// </summary>
        public UIPageHandle Back()
        {
            UILayer[] order = { UILayer.Tips, UILayer.Popup, UILayer.Normal };
            for (int i = 0; i < order.Length; i++)
            {
                UIPageHandle top = Peek(order[i]);
                if (top != null)
                {
                    return top;
                }
            }

            return null;
        }

        /// <summary>取某一层的全部句柄（拷贝，避免调用方改动内部列表）。</summary>
        public List<UIPageHandle> GetHandles(UILayer layer)
        {
            return new List<UIPageHandle>(GetStack(layer));
        }

        /// <summary>取全部句柄（关闭全部页面时使用）。</summary>
        public List<UIPageHandle> GetAllHandles()
        {
            var all = new List<UIPageHandle>();
            foreach (KeyValuePair<UILayer, List<UIPageHandle>> pair in m_layers)
            {
                all.AddRange(pair.Value);
            }

            return all;
        }

        public void Clear()
        {
            m_layers.Clear();
        }

        private List<UIPageHandle> GetStack(UILayer layer)
        {
            List<UIPageHandle> stack;
            if (!m_layers.TryGetValue(layer, out stack))
            {
                stack = new List<UIPageHandle>(4);
                m_layers[layer] = stack;
            }

            return stack;
        }

        private static void SafeInvoke(UIPageHandle handle, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Yato.Core.Log.Error("UIPageStack", string.Format("页面回调异常：{0}", handle.PageId), e);
            }
        }
    }
}
