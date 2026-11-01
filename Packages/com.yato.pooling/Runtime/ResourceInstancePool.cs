using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Pool;
using Yato.Core;
using Yato.Resources;

namespace Yato.Pooling
{
    public interface IPoolCallbacks
    {
        void OnRent();
        void OnReturn();
    }
    /// <summary>A generational value lease. Copies and stale leases cannot return a newer rental. Avoid boxing in hot paths.</summary>
    public readonly struct PooledInstance : IDisposable
    {
        private readonly ResourceInstancePool m_owner;
        private readonly ResourceInstancePool.Slot m_slot;
        private readonly ulong m_generation;
        internal PooledInstance(ResourceInstancePool owner, ResourceInstancePool.Slot slot)
        { m_owner = owner; m_slot = slot; m_generation = slot.Generation; }
        public GameObject GameObject => m_owner != null && m_owner.Valid(m_slot, m_generation) ? m_slot.Object : null;
        public bool IsValid => GameObject != null;
        public void Dispose() { m_owner?.Return(m_slot, m_generation); }
    }

    /// <summary>Main-thread, resource-owned bounded GameObject pool. Track the pool itself in a ResourceScope.</summary>
    public sealed class ResourceInstancePool : IDisposable, IDiagnosticSource
    {
        internal sealed class Slot
        {
            internal GameObject Object;
            internal IPoolCallbacks[] Callbacks;
            internal PoolMember Member;
            internal bool Active;
            internal ulong Generation;
        }
        private readonly IAssetHandle<GameObject> m_prefab;
        private readonly Transform m_parent;
        private readonly ObjectPool<Slot> m_pool;
        private readonly List<Slot> m_slots;
        private bool m_disposed;
        private readonly IDisposable m_diagnostics;
        private string m_lastFailure;
        public DiagnosticSnapshot CaptureDiagnostics() => new DiagnosticSnapshot("Pooling", m_prefab.Key.ToString(), m_disposed ? "Disposed" : "Ready", m_lastFailure,
            new Dictionary<string, string> { ["Capacity"] = Capacity.ToString(), ["Active"] = ActiveCount.ToString(), ["Peak"] = PeakActiveCount.ToString(), ["Created"] = CreatedCount.ToString(), ["Inactive"] = m_pool.CountInactive.ToString() });
        public int Capacity { get; }
        public int ActiveCount { get; private set; }
        public int PeakActiveCount { get; private set; }
        public int CreatedCount => m_disposed ? 0 : m_pool.CountAll;

        private ResourceInstancePool(IAssetHandle<GameObject> prefab, int capacity, Transform parent)
        {
            if (prefab?.Asset == null) throw new ArgumentException("Missing prefab handle");
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            m_prefab = prefab; Capacity = capacity; m_parent = parent;
            m_slots = new List<Slot>(capacity);
            m_pool = new ObjectPool<Slot>(CreateSlot, null, Deactivate, null, false, capacity, capacity);
            m_diagnostics = DiagnosticRegistry.Register(this);
        }

        public static IAwaitable<ResourceInstancePool> CreateAsync(IResourceService resources, AssetKey key,
            int capacity = 64, Transform parent = null, CancellationToken ct = default)
        {
            var source = new Yato.Resources.AwaitableCompletionSource<ResourceInstancePool>();
            try
            {
                if (resources == null) throw new ArgumentNullException(nameof(resources));
                if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
                var loading = resources.LoadAsync<GameObject>(key, ct);
                loading.Completed += () =>
                {
                    IAssetHandle<GameObject> handle = null;
                    try
                    {
                        handle = loading.GetResult(); ct.ThrowIfCancellationRequested();
                        var pool = new ResourceInstancePool(handle, capacity, parent);
                        handle = null; source.SetResult(pool);
                    }
                    catch (Exception error) { handle?.Dispose(); source.SetException(error); }
                };
            }
            catch (Exception error) { source.SetException(error); }
            return source;
        }

        private Slot CreateSlot()
        {
            var slot = new Slot();
            BuildObject(slot);
            m_slots.Add(slot);
            return slot;
        }
        private void BuildObject(Slot slot)
        {
            var instance = UnityEngine.Object.Instantiate(m_prefab.Asset, m_parent, false);
            slot.Object = instance;
            slot.Member = instance.AddComponent<PoolMember>();
            slot.Member.Destroyed = () => OnDestroyed(slot);
            var callbacks = new List<IPoolCallbacks>();
            foreach (var component in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (component is IPoolCallbacks callback) callbacks.Add(callback);
            slot.Callbacks = callbacks.ToArray();
            instance.SetActive(false);
        }
        private void OnDestroyed(Slot slot)
        {
            slot.Object = null; slot.Member = null; slot.Callbacks = Array.Empty<IPoolCallbacks>();
            if (!slot.Active || m_disposed) return;
            slot.Active = false; slot.Generation++; ActiveCount--;
            m_pool.Release(slot);
        }
        private static void Deactivate(Slot slot)
        {
            if (slot.Object == null) return;
            foreach (var callback in slot.Callbacks)
                try { callback.OnReturn(); } catch (Exception error) { Log.Error("Pooling", "OnReturn failed", error); }
            if (slot.Object != null) slot.Object.SetActive(false);
        }
        internal bool Valid(Slot slot, ulong generation) => !m_disposed && slot != null && slot.Active && slot.Generation == generation && slot.Object != null;
        internal void Return(Slot slot, ulong generation)
        {
            if (!Valid(slot, generation)) return;
            slot.Active = false; slot.Generation++; ActiveCount--;
            m_pool.Release(slot);
        }
        public bool TryRent(out PooledInstance instance)
        {
            instance = default;
            if (m_disposed) throw new ObjectDisposedException(nameof(ResourceInstancePool));
            if (m_pool.CountInactive == 0 && m_pool.CountAll >= Capacity) return false;
            var slot = m_pool.Get();
            if (slot.Object == null) BuildObject(slot);
            slot.Active = true; slot.Generation++; ActiveCount++;
            PeakActiveCount = Math.Max(PeakActiveCount, ActiveCount);
            var lease = new PooledInstance(this, slot);
            try
            {
                slot.Object.SetActive(true);
                foreach (var callback in slot.Callbacks) callback.OnRent();
                if (!lease.IsValid) return false;
                instance = lease; return true;
            }
            catch (Exception error) { m_lastFailure = error.ToString(); lease.Dispose(); throw; }
        }
        public void Prewarm(int count)
        {
            if (m_disposed) throw new ObjectDisposedException(nameof(ResourceInstancePool));
            if (count < 0 || count > Capacity) throw new ArgumentOutOfRangeException(nameof(count));
            if (m_pool.CountAll >= count) return;
            var slots = new Slot[count - ActiveCount];
            int taken = 0;
            try { for (; taken < slots.Length; taken++) slots[taken] = m_pool.Get(); }
            finally { for (int i = 0; i < taken; i++) m_pool.Release(slots[i]); }
        }
        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true; m_diagnostics.Dispose();
            foreach (var slot in m_slots)
            {
                slot.Active = false; slot.Generation++;
                if (slot.Member != null) slot.Member.Destroyed = null;
                if (slot.Object != null) UnityEngine.Object.Destroy(slot.Object);
                slot.Object = null; slot.Callbacks = Array.Empty<IPoolCallbacks>();
            }
            ActiveCount = 0;
            m_pool.Clear(); m_slots.Clear(); m_prefab.Dispose();
        }
    }
}
