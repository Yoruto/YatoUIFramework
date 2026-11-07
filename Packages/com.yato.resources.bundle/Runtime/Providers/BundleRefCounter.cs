using System;
using System.Collections.Generic;
using UnityEngine;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>
    /// bundle 引用计数与延迟卸载（文档 3.3 / 6.2）。
    /// 计数归零后进入 HashSet 去重的延迟队列，到期只调用 <c>Unload(false)</c>（RES-02/RES-05/RES-06）。
    /// </summary>
    public sealed class BundleRefCounter
    {
        private readonly Dictionary<string, BundleEntry> m_entries =
            new Dictionary<string, BundleEntry>(StringComparer.Ordinal);

        /// <summary>待卸载 bundle → 到期时间（unscaledTime）。</summary>
        private readonly Dictionary<string, float> m_pending =
            new Dictionary<string, float>(StringComparer.Ordinal);

        private readonly Action<string> m_unloadAction;
        private bool m_hooked;

        /// <summary>延迟卸载秒数，默认 5s。</summary>
        public float UnloadDelaySeconds = 5f;

        public BundleRefCounter(Action<string> unloadAction)
        {
            if (unloadAction == null)
            {
                throw new ArgumentNullException("unloadAction");
            }

            m_unloadAction = unloadAction;
        }

        public IReadOnlyDictionary<string, BundleEntry> Entries
        {
            get { return m_entries; }
        }

        public int PendingCount
        {
            get { return m_pending.Count; }
        }

        /// <summary>接入宿主每帧驱动（幂等）。</summary>
        public void Hook()
        {
            if (m_hooked)
            {
                return;
            }

            RuntimeHost.Updated -= Tick;
            RuntimeHost.Updated += Tick;
            m_hooked = true;
        }

        public void Unhook()
        {
            if (!m_hooked)
            {
                return;
            }

            RuntimeHost.Updated -= Tick;
            m_hooked = false;
        }

        public BundleEntry Get(string bundleName)
        {
            BundleEntry entry;
            return !string.IsNullOrEmpty(bundleName) && m_entries.TryGetValue(bundleName, out entry) ? entry : null;
        }

        /// <summary>取或创建 bundle 记录。</summary>
        public BundleEntry GetOrCreate(string bundleName)
        {
            BundleEntry entry;
            if (!m_entries.TryGetValue(bundleName, out entry))
            {
                entry = new BundleEntry { Name = bundleName };
                m_entries[bundleName] = entry;
            }

            return entry;
        }

        /// <summary>引用 +1，并取消待卸载状态。</summary>
        public int AddRef(string bundleName)
        {
            BundleEntry entry = GetOrCreate(bundleName);
            m_pending.Remove(bundleName);
            entry.AddRef();
            return entry.RefCount;
        }

        /// <summary>
        /// 引用 -1；归零时进入延迟卸载队列（HashSet 语义，重复入队不会产生多条）。
        /// </summary>
        public int Release(string bundleName)
        {
            BundleEntry entry = Get(bundleName);
            if (entry == null)
            {
                return 0;
            }

            int remaining = entry.Release();
            if (remaining <= 0 && !m_pending.ContainsKey(bundleName))
            {
                m_pending[bundleName] = Time.unscaledTime + UnloadDelaySeconds;
                entry.IsPendingUnload = true;
            }

            return remaining;
        }

        /// <summary>记录 bundle 加载完成后的统计信息。</summary>
        public void MarkLoaded(string bundleName, long sizeBytes, float loadSeconds)
        {
            BundleEntry entry = GetOrCreate(bundleName);
            entry.IsLoaded = true;
            entry.SizeBytes = sizeBytes;
            entry.LoadSeconds = loadSeconds;
            entry.IsPendingUnload = false;
        }

        /// <summary>立即卸载所有到期项，返回卸载数量。</summary>
        public int FlushExpired()
        {
            if (m_pending.Count == 0)
            {
                return 0;
            }

            float now = Time.unscaledTime;
            List<string> expired = null;
            foreach (KeyValuePair<string, float> pair in m_pending)
            {
                if (pair.Value <= now)
                {
                    if (expired == null)
                    {
                        expired = new List<string>();
                    }

                    expired.Add(pair.Key);
                }
            }

            if (expired == null)
            {
                return 0;
            }

            for (int i = 0; i < expired.Count; i++)
            {
                string name = expired[i];
                m_pending.Remove(name);
                BundleEntry entry = Get(name);
                if (entry != null)
                {
                    entry.IsLoaded = false;
                    entry.IsPendingUnload = false;
                    entry.SizeBytes = 0;
                }

                try
                {
                    m_unloadAction(name);
                }
                catch (Exception e)
                {
                    Log.Error("BundleRefCounter", "卸载 bundle 失败：" + name, e);
                }
            }

            return expired.Count;
        }

        /// <summary>立即卸载全部（场景切换 / Shutdown）。</summary>
        public void FlushAll()
        {
            var names = new List<string>(m_entries.Keys);
            m_pending.Clear();
            for (int i = 0; i < names.Count; i++)
            {
                BundleEntry entry = Get(names[i]);
                if (entry != null)
                {
                    entry.IsLoaded = false;
                    entry.IsPendingUnload = false;
                }

                try
                {
                    m_unloadAction(names[i]);
                }
                catch (Exception e)
                {
                    Log.Error("BundleRefCounter", "卸载 bundle 失败：" + names[i], e);
                }
            }

            m_entries.Clear();
        }

        /// <summary>生成统计快照。</summary>
        public ResourceStats CreateStats()
        {
            var stats = new ResourceStats();
            long total = 0;
            int loaded = 0;

            foreach (KeyValuePair<string, BundleEntry> pair in m_entries)
            {
                BundleEntry entry = pair.Value;
                if (entry.IsLoaded)
                {
                    loaded++;
                    total += entry.SizeBytes;
                }

                stats.Bundles.Add(new ResourceStats.BundleStat
                {
                    Name = entry.Name,
                    RefCount = entry.RefCount,
                    SizeBytes = entry.SizeBytes,
                    LoadSeconds = entry.LoadSeconds,
                    IsLoaded = entry.IsLoaded
                });
            }

            stats.LoadedBundleCount = loaded;
            stats.TotalBundleBytes = total;
            return stats;
        }

        private void Tick(float deltaTime)
        {
            FlushExpired();
        }
    }
}
