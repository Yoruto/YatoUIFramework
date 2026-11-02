using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yato.Core
{
    public interface IDiagnosticSource
    {
        DiagnosticSnapshot CaptureDiagnostics();
    }

    /// <summary>Detached, read-only data for tools. Capture is explicit and may allocate.</summary>
    public sealed class DiagnosticSnapshot
    {
        public string Module { get; }
        public string Name { get; }
        public string State { get; }
        public string LastError { get; }
        public IReadOnlyDictionary<string, string> Values { get; }
        public DiagnosticSnapshot(string module, string name, string state, string lastError = null,
            IDictionary<string, string> values = null)
        {
            Module = module ?? ""; Name = name ?? ""; State = state ?? ""; LastError = lastError;
            Values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                values == null ? new Dictionary<string, string>() : new Dictionary<string, string>(values));
        }
    }

    /// <summary>Capture on the main thread. Weak registrations cannot keep a service alive.</summary>
    public static class DiagnosticRegistry
    {
        private static readonly Dictionary<long, WeakReference<IDiagnosticSource>> Sources =
            new Dictionary<long, WeakReference<IDiagnosticSource>>();
        private static readonly object Gate = new object();
        private static long s_sequence;
        public static IDisposable Register(IDiagnosticSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            lock (Gate)
            {
                long id = ++s_sequence;
                Sources.Add(id, new WeakReference<IDiagnosticSource>(source));
                return new Registration(id);
            }
        }
        public static IReadOnlyList<DiagnosticSnapshot> Capture()
        {
            var live = new List<IDiagnosticSource>();
            lock (Gate)
            {
                var dead = new List<long>();
                foreach (var pair in Sources)
                    if (pair.Value.TryGetTarget(out var source)) live.Add(source); else dead.Add(pair.Key);
                foreach (long id in dead) Sources.Remove(id);
            }
            var snapshots = new List<DiagnosticSnapshot>(live.Count);
            foreach (var source in live)
                try
                {
                    var snapshot = source.CaptureDiagnostics();
                    if (snapshot != null) snapshots.Add(snapshot);
                }
                catch (Exception error)
                {
                    snapshots.Add(new DiagnosticSnapshot("Diagnostics", source.GetType().Name, "Capture failed", error.Message));
                }
            return snapshots.AsReadOnly();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { lock (Gate) Sources.Clear(); } // IDs remain monotonic across no-reload sessions.
        private sealed class Registration : IDisposable
        {
            private readonly long m_id;
            internal Registration(long id) { m_id = id; }
            public void Dispose() { lock (Gate) Sources.Remove(m_id); }
        }
    }
}
