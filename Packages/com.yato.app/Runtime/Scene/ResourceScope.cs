using System;
using System.Collections.Generic;
using Yato.Core;

namespace Yato.App
{
    /// <summary>
    /// 显式资源所有权范围（D03）：场景/全局各自持有一个范围，统一登记与释放句柄。
    /// <para>释放后范围视为失效：此时迟到的异步加载结果在 <see cref="Track{T}"/> 时立即释放。</para>
    /// <para>共享资源的引用计数由资源层负责，这里只保证「每个句柄恰好在范围内释放一次」。</para>
    /// </summary>
    public sealed class ResourceScope : IDisposable
    {
        private readonly List<IDisposable> m_tracked = new List<IDisposable>();
        private bool m_released;

        public ResourceScope(string name = "scope")
        {
            Name = string.IsNullOrEmpty(name) ? "scope" : name;
        }

        /// <summary>诊断名（通常是场景名）。</summary>
        public string Name { get; private set; }

        /// <summary>是否已释放；释放后 Track 会立即回收资源。</summary>
        public bool IsReleased
        {
            get { return m_released; }
        }

        /// <summary>当前登记的资源数量。</summary>
        public int Count
        {
            get { return m_tracked.Count; }
        }

        /// <summary>登记一个资源句柄并原样返回，便于链式使用。</summary>
        public T Track<T>(T resource) where T : class, IDisposable
        {
            if (resource == null)
            {
                return null;
            }

            if (m_released)
            {
                // 迟到的加载结果：范围已结束，立即释放避免悬挂
                DisposeSafely(resource);
                return resource;
            }

            m_tracked.Add(resource);
            return resource;
        }

        /// <summary>提前释放单个资源并从范围移除。</summary>
        public bool Release(IDisposable resource)
        {
            if (resource == null || !m_tracked.Remove(resource))
            {
                return false;
            }

            DisposeSafely(resource);
            return true;
        }

        /// <summary>按登记的反向顺序释放全部资源；可重复调用。</summary>
        public void ReleaseAll()
        {
            m_released = true;

            for (int i = m_tracked.Count - 1; i >= 0; i--)
            {
                DisposeSafely(m_tracked[i]);
            }

            m_tracked.Clear();
        }

        public void Dispose()
        {
            ReleaseAll();
        }

        private void DisposeSafely(IDisposable resource)
        {
            if (resource == null)
            {
                return;
            }

            try
            {
                resource.Dispose();
            }
            catch (Exception e)
            {
                Log.Error("ResourceScope", string.Format("[{0}] 释放资源失败", Name), e);
            }
        }
    }
}
