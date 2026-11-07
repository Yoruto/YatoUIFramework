using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using Yato.Core;

namespace Yato.Resources
{
    /// <summary>
    /// Process-wide single-writer gate for update transactions and cache maintenance.
    /// A coroutine stopped by host destruction may never reach its finally block, so the
    /// gate exposes a bounded lease that can be reclaimed without restarting the process.
    /// Tokens make sure a late Exit from an abandoned owner cannot release a newer lease.
    /// </summary>
    internal static class HotUpdateGate
    {
        private static readonly object Gate = new object();
        private static bool s_busy;
        private static int s_token;
        private static long s_acquired;

        internal static bool TryEnter(int leaseSeconds, out int token)
        {
            lock (Gate)
            {
                token = 0;
                if (!s_busy)
                {
                    s_busy = true;
                    token = ++s_token;
                    s_acquired = Stopwatch.GetTimestamp();
                    return true;
                }

                if (leaseSeconds <= 0 || s_acquired == 0) return false;
                double elapsed = (Stopwatch.GetTimestamp() - s_acquired) / (double)Stopwatch.Frequency;
                if (elapsed < leaseSeconds) return false;

                // The previous owner is presumed abandoned. Its late Exit is ignored by token.
                token = ++s_token;
                s_acquired = Stopwatch.GetTimestamp();
                Log.Warn(string.Format(
                    "[HotUpdate] Reclaimed an update lease held for {0:F1}s (limit {1}s); the previous async operation did not complete.",
                    elapsed, leaseSeconds));
                return true;
            }
        }

        internal static void Exit(int token)
        {
            lock (Gate)
            {
                if (token == 0 || token != s_token) return;
                s_busy = false;
                s_acquired = 0;
            }
        }

        /// <summary>Refreshes the lease of the current owner. Called as work actually progresses.</summary>
        internal static void Touch(int token)
        {
            lock (Gate)
            {
                if (token == 0 || token != s_token) return;
                s_acquired = Stopwatch.GetTimestamp();
            }
        }

        internal static void Reset()
        {
            lock (Gate)
            {
                s_busy = false;
                s_acquired = 0;
                s_token++;
            }
        }
    }

    public sealed class CacheCleanupResult
    {
        public bool Success;
        public bool Busy;
        public bool InUse;
        public int DeletedDirectories;
        public int RetainedDirectories;
        public int SkippedDirectories;
        public long DeletedBytes;
        public string Error;
        public override string ToString() => $"success={Success} busy={Busy} inUse={InUse} deleted={DeletedDirectories} retained={RetainedDirectories} skipped={SkippedDirectories} bytes={DeletedBytes} error={Error}";
    }

    /// <summary>Explicit startup-only maintenance. Once a loader uses a root it stays protected for this process/session.</summary>
    public static class HotUpdateMaintenance
    {
        private static readonly HashSet<string> Used = new HashSet<string>(Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        private static readonly object Gate = new object();

        internal static void MarkUsed(string directory)
        {
            if (directory.Contains("://") || directory.StartsWith("jar:", StringComparison.Ordinal)) return;
            string full = Path.GetFullPath(directory);
            if (full.Length > Path.GetPathRoot(full).Length) full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            lock (Gate) Used.Add(full);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            lock (Gate) Used.Clear();
            HotUpdateGate.Reset();
        }

        public static CacheCleanupResult PrepareStartup(string root = null)
        {
            var result = new CacheCleanupResult();
            int token;
            if (!HotUpdateGate.TryEnter(0, out token)) { result.Busy = true; return result; }
            try
            {
                var storage = new HotUpdateStorage(root ?? RuntimePaths.PersistentBundleDir);
                lock (Gate)
                {
                    var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    foreach (string used in Used)
                        if (used.Equals(storage.Root, comparison) || used.StartsWith(storage.Root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? storage.Root : storage.Root + Path.DirectorySeparatorChar, comparison))
                        { result.InUse = true; return result; }
                    Cleanup(storage, result);
                }
                result.Success = true;
            }
            catch (Exception error) { result.Error = error.ToString(); }
            finally { HotUpdateGate.Exit(token); }
            return result;
        }

        private static void Cleanup(HotUpdateStorage storage, CacheCleanupResult result)
        {
            string versions = BundleRelativePath.Resolve(storage.Root, "versions");
            string pointer = BundleRelativePath.Resolve(storage.Root, HotUpdateStorage.PointerFile);
            if (Directory.Exists(pointer)) throw new IOException("Active pointer is a directory");
            var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(pointer))
            {
                if (new FileInfo(pointer).Length > 4096) throw new IOException("Oversized active pointer");
                string[] lines = File.ReadAllLines(pointer);
                if (lines.Length != 2 || !Guid.TryParseExact(lines[0], "N", out _))
                    throw new IOException("Invalid active pointer");
                retained.Add(BundleRelativePath.Resolve(storage.Root, "versions/" + lines[0]));
                string previous = Path.GetFullPath(lines[1]);
                if (!string.Equals(previous, storage.Root, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    string id = Path.GetFileName(previous);
                    if (!Guid.TryParseExact(id, "N", out _) ||
                        !string.Equals(previous, BundleRelativePath.Resolve(storage.Root, "versions/" + id), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Invalid previous snapshot pointer");
                    retained.Add(previous);
                }
                foreach (string directory in retained)
                    if (!File.Exists(BundleRelativePath.Resolve(directory, RuntimePaths.ManifestFileName)))
                        throw new IOException("Protected snapshot manifest missing: " + directory);
            }
            else if (Directory.Exists(versions))
            {
                // A missing pointer cannot distinguish an interrupted commit from lost metadata. Retain unknown snapshots.
                if (Directory.GetDirectories(versions).Length > 0) throw new IOException("Active pointer missing; existing snapshots retained");
            }
            var candidates = new List<KeyValuePair<string, long>>();
            if (Directory.Exists(versions))
                foreach (string directory in Directory.GetDirectories(versions))
                {
                    if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) { result.SkippedDirectories++; continue; }
                    long bytes = Measure(directory); // Preflight every candidate before deleting anything.
                    if (retained.Contains(directory)) { result.RetainedDirectories++; continue; }
                    candidates.Add(new KeyValuePair<string, long>(directory, bytes));
                }
            foreach (var candidate in candidates)
            {
                DeleteTree(candidate.Key);
                result.DeletedDirectories++;
                result.DeletedBytes += candidate.Value;
            }
            storage.CleanupStaging();
        }

        private static long Measure(string directory)
        {
            BundleRelativePath.EnsureNoLinks(directory);
            long bytes = 0;
            foreach (string file in Directory.GetFiles(directory))
            { BundleRelativePath.EnsureNoLinks(file); bytes = checked(bytes + new FileInfo(file).Length); }
            foreach (string child in Directory.GetDirectories(directory)) bytes = checked(bytes + Measure(child));
            return bytes;
        }

        private static void DeleteTree(string directory)
        {
            BundleRelativePath.EnsureNoLinks(directory);
            foreach (string child in Directory.GetDirectories(directory)) DeleteTree(child);
            foreach (string file in Directory.GetFiles(directory))
            { BundleRelativePath.EnsureNoLinks(file); File.Delete(file); }
            Directory.Delete(directory);
        }
    }
}
