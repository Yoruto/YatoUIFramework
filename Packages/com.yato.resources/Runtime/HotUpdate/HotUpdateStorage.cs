using System;
using System.IO;
using System.Runtime.ExceptionServices;
using Yato.Core;
using Yato.Persistence;

namespace Yato.Resources
{
    /// <summary>Immutable update snapshots. Only the small active pointer is replaced atomically.</summary>
    public sealed class HotUpdateStorage
    {
        public const string PointerFile = "active.txt";
        public string Root { get; private set; }

        public HotUpdateStorage(string root)
        {
            string full = Path.GetFullPath(root);
            Root = full.Length > Path.GetPathRoot(full).Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
            BundleRelativePath.EnsureNoLinks(Root);
        }

        public string ActiveDirectory
        {
            get
            {
                string directory;
                string error;
                if (TryGetActiveDirectory(out directory, out error)) return directory;
                throw new IOException(error);
            }
        }

        /// <summary>
        /// Resolves the directory backing the current bundle snapshot. When the active pointer is
        /// damaged or its manifest disappeared, the previous snapshot recorded in the same pointer
        /// is used so an interrupted update never makes the last successful version unusable.
        /// The pointer file is never rewritten here.
        /// </summary>
        public bool TryGetActiveDirectory(out string directory, out string error)
        {
            directory = null;
            error = null;
            string pointer = BundleRelativePath.Resolve(Root, PointerFile);
            if (!File.Exists(pointer))
            {
                // Read-only compatibility with the old flat cache.
                directory = Root;
                return true;
            }

            string[] lines = ReadPointerLines(pointer, out string readError);
            if (readError != null)
            {
                error = readError;
                return false;
            }

            if (lines.Length > 0 && TryResolveSnapshot(lines[0], out directory)) return true;
            string activeError = "Invalid active update pointer: " + pointer;
            for (int i = 1; i < lines.Length; i++)
            {
                if (TryResolveSnapshot(lines[i], out directory))
                {
                    error = activeError + "; falling back to previous snapshot " + lines[i];
                    Log.Warn("[HotUpdate] " + error);
                    return true;
                }

                if (IsLegacyRoot(lines[i]))
                {
                    directory = Root;
                    error = activeError + "; falling back to legacy flat cache";
                    Log.Warn("[HotUpdate] " + error);
                    return true;
                }
            }

            error = activeError;
            return false;
        }

        private static string[] ReadPointerLines(string pointer, out string error)
        {
            error = null;
            try
            {
                if (new FileInfo(pointer).Length > 4096)
                {
                    error = "Oversized active update pointer: " + pointer;
                    return Array.Empty<string>();
                }

                return File.ReadAllLines(pointer);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error = "Unreadable active update pointer: " + pointer + " (" + e.Message + ")";
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Accepts both pointer forms: a bare snapshot id (current writer) and an absolute
        /// snapshot directory (legacy writers). Anything outside {root}/versions/&lt;guid&gt; fails.
        /// </summary>
        private bool TryResolveSnapshot(string value, out string directory)
        {
            directory = null;
            string candidate = (value ?? string.Empty).Trim();
            if (candidate.Length == 0) return false;

            if (Guid.TryParseExact(candidate, "N", out _))
                return TrySnapshotDirectory(BundleRelativePath.Resolve(Root, "versions/" + candidate), out directory);

            string full;
            try
            {
                full = Path.GetFullPath(candidate);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                return false;
            }

            if (full.Length > Path.GetPathRoot(full).Length)
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string prefix = BundleRelativePath.Resolve(Root, "versions") + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!full.StartsWith(prefix, comparison)) return false;
            if (!Guid.TryParseExact(full.Substring(prefix.Length), "N", out _)) return false;
            return TrySnapshotDirectory(full, out directory);
        }

        private static bool TrySnapshotDirectory(string candidate, out string directory)
        {
            directory = null;
            if (!File.Exists(BundleRelativePath.Resolve(candidate, RuntimePaths.ManifestFileName))) return false;
            directory = candidate;
            return true;
        }

        private bool IsLegacyRoot(string value)
        {
            string candidate = (value ?? string.Empty).Trim();
            if (candidate.Length == 0 || !Directory.Exists(candidate)) return false;
            string comparisonRoot = Path.GetFullPath(candidate);
            if (comparisonRoot.Length > Path.GetPathRoot(comparisonRoot).Length)
                comparisonRoot = comparisonRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(comparisonRoot, Root, comparison)) return false;
            return File.Exists(BundleRelativePath.Resolve(Root, RuntimePaths.ManifestFileName));
        }

        public string Begin()
        {
            string directory = BundleRelativePath.Resolve(Root, "staging/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        public void Commit(string stagedDirectory)
        {
            if (!TryCommit(stagedDirectory, out Exception error)) ExceptionDispatchInfo.Capture(error).Throw();
        }

        // Only directory promotion may be retried. Pointer replacement failures always escape immediately.
        internal bool TryCommit(string stagedDirectory, out Exception promotionError)
        {
            promotionError = null;
            string id = Path.GetFileName(stagedDirectory);
            if (!Guid.TryParseExact(id, "N", out _) || stagedDirectory != BundleRelativePath.Resolve(Root, "staging/" + id))
                throw new IOException("Unknown update staging directory");
            string old = ActiveDirectory;
            string target = BundleRelativePath.Resolve(Root, "versions/" + id);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            try { Directory.Move(stagedDirectory, target); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Retry only when promotion demonstrably did not happen; never overwrite a snapshot.
                if (!Directory.Exists(stagedDirectory) || Directory.Exists(target) || File.Exists(target)) throw;
                promotionError = e;
                return false;
            }
            try
            {
                // Same failure/flush/atomic replacement guarantees as D04; never delete the old pointer first.
                new FileStore(Root).WriteAllText(PointerFile, id + "\n" + old);
            }
            catch
            {
                DeleteTree(target);
                throw;
            }
            // Committed versions are retained: existing BundleLoaders may still reference their files.
            return true;
        }

        public void CleanupStaging()
        {
            string directory = BundleRelativePath.Resolve(Root, "staging");
            if (!Directory.Exists(directory)) return;
            foreach (string child in Directory.GetDirectories(directory))
                if (Guid.TryParseExact(Path.GetFileName(child), "N", out _)) DeleteTree(child);
        }

        public void Discard(string stagedDirectory)
        {
            if (string.IsNullOrEmpty(stagedDirectory)) return;
            string id = Path.GetFileName(stagedDirectory);
            if (!Guid.TryParseExact(id, "N", out _) || stagedDirectory != BundleRelativePath.Resolve(Root, "staging/" + id))
                throw new IOException("Unknown update staging directory");
            DeleteTree(stagedDirectory);
        }

        private static void DeleteTree(string directory)
        {
            BundleRelativePath.EnsureNoLinks(directory);
            if (!Directory.Exists(directory)) return;
            foreach (string child in Directory.GetDirectories(directory)) DeleteTree(child);
            foreach (string file in Directory.GetFiles(directory))
            {
                BundleRelativePath.EnsureNoLinks(file);
                File.Delete(file);
            }
            Directory.Delete(directory);
        }
    }
}
