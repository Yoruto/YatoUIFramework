using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Yato.Core;

namespace Yato.Survivor
{
    // Opt-in command-line verification fixture. Normal launches create no fixture and change no gameplay values.
    public static class SurvivorPerformanceScenario
    {
        public static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            var output = Argument("--survivor-profile-output");
            if (string.IsNullOrEmpty(output)) return;
            var session = new Measurement(output);
            RuntimeHost.LateUpdated += session.Tick;
            Application.quitting += session.Dispose;
        }
        private sealed class Measurement : IDisposable
        {
            private readonly string m_output;
            private readonly List<double> m_frames = new List<double>(32768);
            private SurvivorGame m_game;
            private bool m_requested, m_prepared, m_done;
            private double m_warm, m_sample, m_sum;
            private long m_bytes;
            private bool m_counterChecked, m_counterSupported;
            private long m_probeDelta, m_managedBefore;
            private int m_gen0Before;
            private int m_minEnemies = int.MaxValue, m_minBullets = int.MaxValue, m_minPickups = int.MaxValue;
            public Measurement(string output) { m_output = Path.GetFullPath(output); }
            public void Tick(float delta)
            {
                if (m_done) return;
                try
                {
                    if (m_game == null) m_game = UnityEngine.Object.FindAnyObjectByType<SurvivorGame>();
                    if (m_game == null || !m_game.Ready || m_game.IsBusy) return;
                    if (!m_requested) { m_requested = true; m_game.RunAction("Btn_Start", null); return; }
                    var session = m_game.Session;
                    if (session == null || session.Stopped || m_game.Services.Pause.IsPaused) return;
                    if (!m_prepared) { session.Progress.Tick(180); m_prepared = true; }
                    session.MaintainPressureScenario();
                    var elapsed = Time.unscaledDeltaTime;
                    if (m_warm < 5) { m_warm += elapsed; return; }
                    if (!m_counterChecked)
                    {
                        var before = GC.GetAllocatedBytesForCurrentThread();
                        var probe = new byte[4096]; GC.KeepAlive(probe);
                        m_probeDelta = GC.GetAllocatedBytesForCurrentThread() - before;
                        m_counterSupported = m_probeDelta >= 4096; m_counterChecked = true;
                        m_bytes = GC.GetAllocatedBytesForCurrentThread(); m_managedBefore = GC.GetTotalMemory(false); m_gen0Before = GC.CollectionCount(0);
                    }
                    var milliseconds = elapsed * 1000.0;
                    m_frames.Add(milliseconds); m_sum += milliseconds; m_sample += elapsed;
                    m_minEnemies = Math.Min(m_minEnemies, session.EnemyCount);
                    m_minBullets = Math.Min(m_minBullets, session.ProjectileCount);
                    m_minPickups = Math.Min(m_minPickups, session.PickupCount);
                    if (m_sample < 60) return;
                    var allocated = GC.GetAllocatedBytesForCurrentThread() - m_bytes;
                    m_frames.Sort();
                    var report = new PerformanceReport
                    {
                        Seconds = m_sample, Frames = m_frames.Count, MeanMilliseconds = m_sum / m_frames.Count,
                        P95Milliseconds = m_frames[(int)((m_frames.Count - 1) * .95)], MainThreadAllocatedBytes = m_counterSupported ? allocated : -1,
                        AllocationCounterSupported = m_counterSupported, AllocationProbeDelta = m_probeDelta,
                        ManagedMemoryBefore = m_managedBefore, ManagedMemoryAfter = GC.GetTotalMemory(false), Gen0Collections = GC.CollectionCount(0) - m_gen0Before,
                        MinimumEnemies = m_minEnemies, MinimumProjectiles = m_minBullets, MinimumPickups = m_minPickups,
                        PoolOverflows = session.PoolOverflows, Width = Screen.width, Height = Screen.height,
                        Quality = QualitySettings.names[QualitySettings.GetQualityLevel()], Cpu = SystemInfo.processorType,
                        Gpu = SystemInfo.graphicsDeviceName, MemoryMegabytes = SystemInfo.systemMemorySize,
                        PoolActiveEnemies = session.EnemyCount, PoolActiveProjectiles = session.ProjectileCount,
                        Notes = "Release rendered pressure fixture; fixed population, excess drops returned; health and pending upgrades stabilized. Allocation bytes are -1 if a 4096-byte positive control fails. Managed heap is net usage, not cumulative allocations. Not a gameplay victory test."
                    };
                    Directory.CreateDirectory(Path.GetDirectoryName(m_output));
                    File.WriteAllText(m_output, JsonUtility.ToJson(report, true));
                    m_done = true; Dispose();
                    session.End(false, "Performance verification complete");
                    RuntimeHost.Updated += ExitWhenReady;
                }
                catch (Exception error)
                {
                    m_done = true; Dispose();
                    File.WriteAllText(m_output, "{\"error\":\"" + error.GetType().Name + "\"}");
                    Debug.LogException(error); Application.Quit(2);
                }
            }
            private void ExitWhenReady(float delta)
            {
                if (m_game == null || m_game.IsBusy || m_game.Session != null) return;
                RuntimeHost.Updated -= ExitWhenReady; m_game.RunAction("Btn_Quit", null);
            }
            public void Dispose() { RuntimeHost.LateUpdated -= Tick; Application.quitting -= Dispose; }
        }
        [Serializable] private sealed class PerformanceReport
        {
            public double Seconds, MeanMilliseconds, P95Milliseconds;
            public int Frames, MinimumEnemies, MinimumProjectiles, MinimumPickups, PoolOverflows, Width, Height,
                MemoryMegabytes, PoolActiveEnemies, PoolActiveProjectiles, Gen0Collections;
            public long MainThreadAllocatedBytes, AllocationProbeDelta, ManagedMemoryBefore, ManagedMemoryAfter;
            public bool AllocationCounterSupported;
            public string Quality, Cpu, Gpu, Notes;
        }
    }
}
