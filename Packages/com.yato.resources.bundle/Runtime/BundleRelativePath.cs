using System;
using System.IO;

namespace Yato.Resources
{
    /// <summary>Portable, canonical bundle names and filesystem confinement. Rejects links, including root ancestors.</summary>
    public static class BundleRelativePath
    {
        public static string Validate(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 240)
                throw new ArgumentException("Invalid relative path: " + value);
            foreach (string part in value.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal))
                    throw new ArgumentException("Invalid path segment: " + value);
                foreach (char c in part)
                    if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                        !(c >= '0' && c <= '9') && c != '_' && c != '-' && c != '.')
                        throw new ArgumentException("Invalid path character: " + value);
                string stem = part.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                    (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                     stem[3] >= '0' && stem[3] <= '9'))
                    throw new ArgumentException("Reserved path: " + value);
            }
            return value;
        }

        public static string ValidateBundle(string value)
        {
            Validate(value);
            string first = value.Split('/')[0];
            if (first.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("version.json", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("active.txt", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("versions", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("staging", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Reserved bundle path: " + value);
            return value;
        }

        public static string Resolve(string root, string relative)
        {
            Validate(relative);
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(prefix, relative));
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!full.StartsWith(prefix, comparison)) throw new IOException("Path escapes resource root: " + relative);
            EnsureNoLinks(full);
            return full;
        }

        public static void EnsureNoLinks(string fullPath)
        {
            string current = Path.GetFullPath(fullPath);
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Resource path contains a symbolic link/reparse point: " + current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                current = Path.GetDirectoryName(current);
            }
        }
    }
}
