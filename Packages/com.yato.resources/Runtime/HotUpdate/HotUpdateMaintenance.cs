using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Yato.Resources
{
    internal static class HotUpdateGate
    {
        private static int s_busy;
        internal static bool TryEnter() => Interlocked.CompareExchange(ref s_busy, 1, 0) == 0;
        internal static void Exit() => Interlocked.Exchange(ref s_busy, 0);
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
            lock (Gate) Used.Add(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { lock (Gate) Used.Clear(); }

        public static CacheCleanupResult PrepareStartup(string root = null)
        {
            var result = new CacheCleanupResult();
            if (!HotUpdateGate.TryEnter()) { result.Busy = true; return result; }
            try
            {
                var storage = new HotUpdateStorage(root ?? RuntimePaths.PersistentBundleDir);
                lock (Gate)
                {
                    var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    foreach (string used in Used)
                        if (used.Equals(storage.Root, comparison) || used.StartsWith(storage.Root + Path.DirectorySeparatorChar, comparison))
                        { result.InUse = true; return result; }
                    Cleanup(storage, result);
                }
                result.Success = true;
            }
            catch (Exception error) { result.Error = error.ToString(); }
            finally { HotUpdateGate.Exit(); }
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
                if (previous != storage.Root)
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
                // Without a committed pointer, only GUID snapshots not referenced by a flat manifest are orphans.
                storage.ActiveDirectory.ToString();
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
