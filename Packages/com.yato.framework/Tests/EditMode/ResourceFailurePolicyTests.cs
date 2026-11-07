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
            var error = Assert.Throws<ResourceException>(() => BundleResourceServiceFactory.CreateProvider(manifestPath: missing));
            StringAssert.Contains(missing, error.Message);
            StringAssert.Contains(RuntimePaths.PlatformName, error.Message);
            StringAssert.Contains("BundleProvider", error.Message);
            Assert.IsNotNull(error.InnerException);
            Assert.Throws<ResourceException>(() => BundleResourceServiceFactory.CreateProvider((ResourceFailurePolicy)999, missing));
            Assert.IsInstanceOf<ResourcesProvider>(BundleResourceServiceFactory.CreateProvider(ResourceFailurePolicy.AllowResourcesFallback, missing));
        }
    }
}
