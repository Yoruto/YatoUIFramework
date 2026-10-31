using System;
using System.IO;
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
            Root = Path.GetFullPath(root);
            BundleRelativePath.EnsureNoLinks(Root);
        }

        public string ActiveDirectory
        {
            get
            {
                string pointer = BundleRelativePath.Resolve(Root, PointerFile);
                if (!File.Exists(pointer)) return Root; // Read-only compatibility with the old flat cache.
                string[] lines = File.ReadAllLines(pointer);
                if (lines.Length == 0 || !Guid.TryParseExact(lines[0], "N", out _))
                    throw new IOException("Invalid active update pointer: " + pointer);
                string directory = BundleRelativePath.Resolve(Root, "versions/" + lines[0]);
                if (!File.Exists(BundleRelativePath.Resolve(directory, RuntimePaths.ManifestFileName)))
                    throw new IOException("Active update manifest is missing: " + directory);
                return directory;
            }
        }

        public string Begin()
        {
            string directory = BundleRelativePath.Resolve(Root, "staging/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        public void Commit(string stagedDirectory)
        {
            string id = Path.GetFileName(stagedDirectory);
            if (!Guid.TryParseExact(id, "N", out _) || stagedDirectory != BundleRelativePath.Resolve(Root, "staging/" + id))
                throw new IOException("Unknown update staging directory");
            string old = ActiveDirectory;
            string target = BundleRelativePath.Resolve(Root, "versions/" + id);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            Directory.Move(stagedDirectory, target);
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
