using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Yato.Resources;

namespace Yato.ModularConsumerTests
{
    // Copied only when Bundle is installed and HotUpdate is absent.
    public sealed class LocalBundlePolicyTests
    {
        [Test]
        public void LocalBundlesDoNotInterpretOrRewriteHotUpdatePointers()
        {
            string root = Path.Combine(Application.temporaryCachePath, "yato-local-only-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string pointer = Path.Combine(root, "active.txt");
            File.WriteAllText(pointer, "deliberately-invalid-update-pointer");
            try
            {
                Assert.AreEqual(root, BundleCachePolicy.Current.GetActiveDirectory(root));
                BundleCachePolicy.Current.MarkUsed(root);
                BundleCachePolicy.Current.PrepareStartup();
                Assert.AreEqual("deliberately-invalid-update-pointer", File.ReadAllText(pointer));
                Assert.AreEqual(1, Directory.GetFileSystemEntries(root).Length);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
