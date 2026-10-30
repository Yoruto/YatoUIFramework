using System;

namespace Yato.Resources
{
    /// <summary>资源加载流水线阶段，用于定位失败点。</summary>
    public enum ResourceStage
    {
        None = 0,
        KeyNormalize = 1,
        ProviderSelect = 2,
        ManifestLoad = 3,
        BundleLoad = 4,
        BundleDownload = 5,
        AssetLoad = 6,
        Instantiate = 7,
        Release = 8
    }

    /// <summary>资源层统一异常：必须携带可诊断上下文（文档 6.1）。</summary>
    public sealed class ResourceException : Exception
    {
        public ResourceException(string message) : this(null, null, ResourceStage.None, message, null)
        {
        }

        public ResourceException(string message, Exception innerException)
            : this(null, null, ResourceStage.None, message, innerException)
        {
        }

        public ResourceException(string assetKey, string bundle, ResourceStage stage, string message, Exception innerException = null)
            : base(message, innerException)
        {
            AssetKey = assetKey;
            Bundle = bundle;
            Stage = stage;
        }

        /// <summary>逻辑资源 key（全小写、斜杠分隔、无扩展名）。</summary>
        public string AssetKey { get; private set; }

        /// <summary>所属 bundle 名；Resources 模式为空。</summary>
        public string Bundle { get; private set; }

        /// <summary>失败阶段。</summary>
        public ResourceStage Stage { get; private set; }

        public override string Message
        {
            get
            {
                return string.Format(
                    "[{0}] key={1} bundle={2} : {3}",
                    Stage,
                    AssetKey ?? "-",
                    string.IsNullOrEmpty(Bundle) ? "-" : Bundle,
                    base.Message);
            }
        }
    }
}
