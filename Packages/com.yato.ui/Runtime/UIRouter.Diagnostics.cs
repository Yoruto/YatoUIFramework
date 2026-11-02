using System;
using System.Collections.Generic;
using Yato.Core;

namespace Yato.UI
{
    public static partial class UIRouter
    {
        private static readonly RouterDiagnostics DiagnosticsSource = new RouterDiagnostics();
        private static IDisposable s_diagnosticRegistration;
        private static void EnsureDiagnostics()
        {
            if (s_diagnosticRegistration == null) s_diagnosticRegistration = DiagnosticRegistry.Register(DiagnosticsSource);
        }
        private sealed class RouterDiagnostics : IDiagnosticSource
        {
            public DiagnosticSnapshot CaptureDiagnostics()
            {
                var pages = new List<string>();
                foreach (var handle in s_stack.GetAllHandles())
                    if (handle != null && handle.IsValid) pages.Add(handle.Context.Layer + ":" + handle.PageId);
                return new DiagnosticSnapshot("UI", "UIRouter", s_pendingOpens.Count > 0 ? "Loading" : "Ready", values:
                    new Dictionary<string, string> {
                        ["Open pages"] = pages.Count.ToString(), ["Stack"] = string.Join(", ", pages),
                        ["Top"] = TopPage?.PageId ?? "none", ["Pending opens"] = s_pendingOpens.Count.ToString(),
                        ["Cached pages"] = s_cache.Count.ToString() });
            }
        }
    }
}
