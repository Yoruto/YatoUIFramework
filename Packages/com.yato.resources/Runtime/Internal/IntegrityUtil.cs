using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Yato.Resources
{
    /// <summary>
    /// Single-pass integrity checks for downloaded bundles and cached snapshots.
    /// CRC32 detects accidental corruption; SHA-256 is an optional provenance check used
    /// when the publisher provides a 64-character hash in the manifest.
    /// </summary>
    internal static class IntegrityUtil
    {
        private static uint[] s_table;
        private static readonly object Gate = new object();

        public static bool Matches(string path, long expectedSize, uint expectedCrc, string expectedSha256)
        {
            BundleRelativePath.EnsureNoLinks(path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != expectedSize) return false;

            uint crc;
            string sha;
            Compute(path, !string.IsNullOrEmpty(expectedSha256), out crc, out sha);
            if (crc != expectedCrc) return false;
            return string.IsNullOrEmpty(expectedSha256) ||
                   string.Equals(sha, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Reads the file once and returns CRC32 plus an optional lowercase SHA-256 hex string.</summary>
        public static void Compute(string path, bool computeSha256, out uint crc, out string sha256)
        {
            sha256 = null;
            uint value = 0xFFFFFFFFu;
            uint[] table = GetTable();
            var buffer = new byte[64 * 1024];
            using (var stream = File.OpenRead(path))
            {
                if (computeSha256)
                {
                    using (var algorithm = SHA256.Create())
                    {
                        int count;
                        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            for (int i = 0; i < count; i++) value = (value >> 8) ^ table[(value ^ buffer[i]) & 0xFF];
                            algorithm.TransformBlock(buffer, 0, count, buffer, 0);
                        }

                        algorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        sha256 = ToHex(algorithm.Hash);
                    }
                }
                else
                {
                    int count;
                    while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                        for (int i = 0; i < count; i++) value = (value >> 8) ^ table[(value ^ buffer[i]) & 0xFF];
                }
            }

            crc = ~value;
        }

        private static uint[] GetTable()
        {
            if (s_table != null) return s_table;
            lock (Gate)
            {
                if (s_table != null) return s_table;
                var table = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint value = i;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                    }

                    table[i] = value;
                }

                s_table = table;
                return s_table;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }
    }
}
