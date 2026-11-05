using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Yato.Editor;
using Yato.Resources;

namespace Yato.Tests.EditMode
{
    public sealed class EditorAssetProviderTests
    {
        private string m_root;

        [SetUp]
        public void Setup()
        {
            m_root = "Assets/YatoStrictKeyTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(m_root));
            AssetDatabase.CreateFolder(m_root, "config");
        }

        [TearDown]
        public void Teardown() => AssetDatabase.DeleteAsset(m_root);

        [TestCase(".txt")]
        [TestCase(".json")]
        [TestCase(".bytes")]
        [TestCase(".csv")]
        public void StrictKeys_LoadsBuildableTextAssetsByFullKey(string extension)
        {
            string path = m_root + "/config/table" + extension;
            File.WriteAllText(path, "{\"id\":1}");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Assert.IsTrue(BundleNaming.IsBuildable(path));
            var provider = new EditorAssetProvider(true, m_root);
            var key = new AssetKey("config/table");
            var operation = provider.LoadAsync(key, typeof(TextAsset), CancellationToken.None);
            Assert.IsTrue(operation.IsDone);
            var asset = (TextAsset)operation.GetResult();
            try
            {
                Assert.AreEqual(path, AssetDatabase.GetAssetPath(asset));
                Assert.AreEqual("{\"id\":1}", asset.text);
            }
            finally { provider.Release(key); }
        }

        [Test]
        public void StrictKeys_LoadsAnimationAndBaseObjectUsingTheBuildableExtensions()
        {
            string path = m_root + "/config/motion.anim";
            AssetDatabase.CreateAsset(new AnimationClip(), path);
            var provider = new EditorAssetProvider(true, m_root);
            var key = new AssetKey("config/motion");
            foreach (var type in new[] { typeof(AnimationClip), typeof(UnityEngine.Object) })
            {
                var asset = provider.LoadAsync(key, type, CancellationToken.None).GetResult();
                try { Assert.AreEqual(path, AssetDatabase.GetAssetPath(asset)); }
                finally { provider.Release(key); }
            }
        }

        [Test]
        public void StrictKeys_DoesNotFallBackToAnAssetWithTheSameLeafName()
        {
            string path = m_root + "/config/table.txt";
            File.WriteAllText(path, "table");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var provider = new EditorAssetProvider(true, m_root);
            var operation = provider.LoadAsync(new AssetKey("wrong/table"), typeof(TextAsset), CancellationToken.None);
            Assert.Throws<ResourceException>(() => operation.GetResult());
            Assert.AreEqual(0, provider.Stats.ActiveAssetCount);
        }
    }
}
