using System.IO;

namespace Yato.Resources
{
    /// <summary>运行时 CRC32（热更校验用，与 Editor/ManifestBuilder 的算法一致）。</summary>
    public static class Crc32Util
    {
        private static uint[] s_table;

        public static uint Compute(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return 0u;
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            return Compute(bytes, 0, bytes.Length);
        }

        public static uint Compute(byte[] buffer, int offset, int count)
        {
            uint[] table = GetTable();
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < count; i++)
            {
                crc = (crc >> 8) ^ table[(crc ^ buffer[offset + i]) & 0xFF];
            }

            return ~crc;
        }

        private static uint[] GetTable()
        {
            if (s_table != null)
            {
                return s_table;
            }

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
}
