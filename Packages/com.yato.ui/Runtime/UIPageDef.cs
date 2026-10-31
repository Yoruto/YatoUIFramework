using System;
using Yato.Resources;

namespace Yato.UI
{
    /// <summary>页面静态定义（文档 3.6）。由代码生成器产出在 UIPages.g.cs 中。</summary>
    public sealed class UIPageDef
    {
        /// <summary>页面唯一标识，如 "BagForm"。</summary>
        public string PageId;

        /// <summary>中介者类型；为 null 时直接驱动 View。</summary>
        public Type MediatorType;

        /// <summary>预制体资源 key，如 "ui/bag/bagform"。</summary>
        public AssetKey PrefabKey;

        /// <summary>默认层级。</summary>
        public UILayer Layer;

        /// <summary>关闭后是否缓存实例（再次打开不重新实例化）。</summary>
        public bool Cacheable;

        /// <summary>是否禁止重复打开；false 时同一页面可同时存在多份。</summary>
        public bool Singleton;

        public UIPageDef(
            string pageId,
            Type mediatorType,
            AssetKey prefabKey,
            UILayer layer = UILayer.Normal,
            bool cacheable = false,
            bool singleton = true)
        {
            PageId = pageId;
            MediatorType = mediatorType;
            PrefabKey = prefabKey;
            Layer = layer;
            Cacheable = cacheable;
            Singleton = singleton;
        }

        public bool IsValid
        {
            get { return !string.IsNullOrEmpty(PageId) && PrefabKey.IsValid; }
        }

        public override string ToString()
        {
            return string.Format("{0}({1}, {2})", PageId, Layer, PrefabKey);
        }
    }
}
