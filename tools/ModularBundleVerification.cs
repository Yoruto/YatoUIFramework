using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using Yato.Resources;

namespace Yato
{
    public static partial class ModularConsumerVerification
    {
        static partial void PrepareOptionalBundles()
        {
            const string bundleName = "consumer/fixture";
            string output = Path.Combine("Assets/StreamingAssets/YatoModular/AssetBundles", RuntimePaths.PlatformName);
            Directory.CreateDirectory(output);
            var builds = new[] { new AssetBundleBuild
            {
                assetBundleName = bundleName,
                assetNames = new[] { PrefabPath },
                addressableNames = new[] { Key }
            } };
            if (BuildPipeline.BuildAssetBundles(output, builds, BuildAssetBundleOptions.ChunkBasedCompression, EditorUserBuildSettings.activeBuildTarget) == null)
                throw new InvalidOperationException("Modular consumer AssetBundle build failed");
            string bundlePath = Path.Combine(output, bundleName);
            byte[] bytes = File.ReadAllBytes(bundlePath);
            uint crc = Crc32(bytes);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            string bundle = "\"" + bundleName + "\":{\"hash\":\"" + hash + "\",\"crc\":" + crc +
                ",\"size\":" + bytes.LongLength + ",\"compression\":\"LZ4\",\"deps\":[],\"assets\":[\"" + Key + "\"]}";
            string manifest = "{\"schema\":2,\"version\":\"consumer-1\",\"platform\":\"" + RuntimePaths.PlatformName +
                "\",\"bundles\":{" + bundle + "},\"assets\":{\"" + Key + "\":\"" + bundleName + "\"}}";
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(output, "manifest.json"), manifest, utf8);
            string version = "{\"schema\":1,\"appVersion\":\"consumer-1\",\"platform\":\"" + RuntimePaths.PlatformName +
                "\",\"manifestCrc\":" + Crc32(utf8.GetBytes(manifest)) + ",\"bundles\":{" + bundle + "}}";
            File.WriteAllText(Path.Combine(output, "version.json"), version, utf8);
            AssetDatabase.Refresh();
        }

        private static uint Crc32(byte[] bytes)
        {
            uint crc = uint.MaxValue;
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }
    }
}
