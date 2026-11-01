using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yato.Core;

namespace Yato.Tests.EditMode
{
    public sealed class DiagnosticRegistryTests
    {
        private sealed class Source : IDiagnosticSource
        {
            internal string Name = Guid.NewGuid().ToString("N");
            internal bool Throw;
            internal Dictionary<string, string> Values = new Dictionary<string, string> { ["count"] = "1" };
            public DiagnosticSnapshot CaptureDiagnostics()
            {
                if (Throw) throw new InvalidOperationException("capture fault");
                return new DiagnosticSnapshot("Registry test", Name, "Ready", values: Values);
            }
        }
        [Test] public void SnapshotsAreDetachedAndDisposalIsIdempotent()
        {
            var source = new Source();
            var registration = DiagnosticRegistry.Register(source);
            var snapshot = DiagnosticRegistry.Capture().Single(x => x.Name == source.Name);
            source.Values["count"] = "2";
            Assert.AreEqual("1", snapshot.Values["count"]);
            registration.Dispose(); registration.Dispose();
            Assert.IsFalse(DiagnosticRegistry.Capture().Any(x => x.Name == source.Name));
        }
        [Test] public void OneFailingSourceDoesNotHideOtherModules()
        {
            var bad = new Source { Throw = true }; var good = new Source();
            using (DiagnosticRegistry.Register(bad))
            using (DiagnosticRegistry.Register(good))
            {
                var snapshot = DiagnosticRegistry.Capture();
                Assert.IsTrue(snapshot.Any(x => x.Name == good.Name));
                Assert.IsTrue(snapshot.Any(x => x.LastError == "capture fault"));
            }
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static WeakReference CreateWeakSource()
        {
            var source = new Source();
            DiagnosticRegistry.Register(source);
            return new WeakReference(source);
        }
        [Test] public void RegistryDoesNotRetainAnAbandonedService()
        {
            var weak = CreateWeakSource();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.IsFalse(weak.IsAlive);
            Assert.DoesNotThrow(() => DiagnosticRegistry.Capture());
        }
    }
}
