using System;
using System.IO;
using NUnit.Framework;
using Yato.Resources;

namespace Yato.Tests.EditMode
{
    public sealed class ResourceFailurePolicyTests
    {
        [Test] public void MissingManifest_StrictByDefault_ExplicitFallbackOnly()
        {
            string missing = Path.Combine(Path.GetTempPath(), "yato-missing-" + Guid.NewGuid().ToString("N"), "manifest.json");
            bool before = ResourceServiceFactory.ForceBundleMode;
            try
            {
                ResourceServiceFactory.ForceBundleMode = true;
                var error = Assert.Throws<ResourceException>(() => ResourceServiceFactory.CreateProvider(manifestPath: missing));
                StringAssert.Contains(missing, error.Message);
                StringAssert.Contains(RuntimePaths.PlatformName, error.Message);
                StringAssert.Contains("BundleProvider", error.Message);
                Assert.IsNotNull(error.InnerException);
                Assert.Throws<ResourceException>(() => ResourceServiceFactory.CreateProvider((ResourceFailurePolicy)999, missing));
                Assert.IsInstanceOf<ResourcesProvider>(ResourceServiceFactory.CreateProvider(ResourceFailurePolicy.AllowResourcesFallback, missing));
            }
            finally { ResourceServiceFactory.ForceBundleMode = before; }
        }
    }
}
