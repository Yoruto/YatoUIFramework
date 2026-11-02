using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yato.Resources;

namespace Yato.Pooling.Tests
{
    public sealed class ResourceInstancePoolTests
    {
        private GameObject m_prefab;
        private ResourceInstancePool m_pool;
        [SetUp] public void Setup() { m_prefab = new GameObject("Pool test prefab"); m_prefab.SetActive(false); }
        [TearDown] public void Teardown() { m_pool?.Dispose(); UnityEngine.Object.Destroy(m_prefab); }
        private sealed class Provider : IResourceProvider
        {
            public GameObject Prefab;
            public int Releases;
            public bool Delayed;
            public readonly Yato.Resources.AwaitableCompletionSource<UnityEngine.Object> Pending = new Yato.Resources.AwaitableCompletionSource<UnityEngine.Object>();
            public ResourceStats Stats { get; } = new ResourceStats();
            public IAwaitable<UnityEngine.Object> LoadAsync(AssetKey key, Type type, CancellationToken ct)
            { if (!Delayed) Pending.SetResult(Prefab); return Pending; }
            public void Release(AssetKey key) { Releases++; }
            public void ReleaseUnused() { }
        }
        [UnityTest] public IEnumerator CapacityStaleCopiesAndDisposal_AreSafe()
        {
            var provider = new Provider { Prefab = m_prefab };
            var creating = ResourceInstancePool.CreateAsync(new ResourceService(provider), new AssetKey("pool/test"), 2);
            yield return creating.AsEnumerator();
            m_pool = creating.GetResult(); m_pool.Prewarm(2);
            Assert.IsTrue(m_pool.TryRent(out var first)); var copy = first;
            GameObject original = first.GameObject;
            Assert.IsTrue(m_pool.TryRent(out var second));
            Assert.IsFalse(m_pool.TryRent(out _));
            first.Dispose();
            Assert.IsTrue(m_pool.TryRent(out var third));
            Assert.AreSame(original, third.GameObject);
            copy.Dispose();
            Assert.AreEqual(2, m_pool.ActiveCount);
            third.Dispose(); second.Dispose();
            m_pool.Dispose(); m_pool.Dispose();
            Assert.IsFalse(third.IsValid);
            Assert.AreEqual(1, provider.Releases);
        }
        [UnityTest] public IEnumerator ExternallyDestroyedRental_IsReplacedWithinCapacity()
        {
            var provider = new Provider { Prefab = m_prefab };
            var creating = ResourceInstancePool.CreateAsync(new ResourceService(provider), new AssetKey("pool/test"), 1);
            yield return creating.AsEnumerator();
            m_pool = creating.GetResult();
            m_pool.TryRent(out var first); UnityEngine.Object.Destroy(first.GameObject);
            yield return null;
            Assert.AreEqual(0, m_pool.ActiveCount);
            Assert.IsTrue(m_pool.TryRent(out var second));
            Assert.IsTrue(second.IsValid); Assert.IsFalse(first.IsValid);
            second.Dispose();
        }
        [UnityTest] public IEnumerator CanceledCreation_ReleasesLateHandle()
        {
            var provider = new Provider { Prefab = m_prefab, Delayed = true };
            using (var cts = new CancellationTokenSource())
            {
                var creating = ResourceInstancePool.CreateAsync(new ResourceService(provider), new AssetKey("pool/test"), ct: cts.Token);
                cts.Cancel(); provider.Pending.SetResult(m_prefab);
                yield return creating.AsEnumerator();
                Assert.Throws<OperationCanceledException>(() => creating.GetResult());
                Assert.AreEqual(1, provider.Releases);
            }
        }
        [UnityTest] public IEnumerator WarmRentReturn_HasNoManagedAllocations()
        {
            var creating = ResourceInstancePool.CreateAsync(new ResourceService(new Provider { Prefab = m_prefab }), new AssetKey("pool/test"), 4);
            yield return creating.AsEnumerator(); m_pool = creating.GetResult(); m_pool.Prewarm(4);
            for (int i = 0; i < 100; i++) { m_pool.TryRent(out var item); item.Dispose(); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { m_pool.TryRent(out var item); item.Dispose(); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
            Assert.AreEqual(0, m_pool.ActiveCount);
            Assert.AreEqual(4, m_pool.CreatedCount);
            Debug.Log("[Yato.Pooling] 1000 warm rent/return operations: " + allocated + " managed bytes");
        }
    }
}
