using System;
using System.Collections.Generic;
using UnityEngine;
using Yato.Core;
using Yato.Resources;

namespace Yato.UI
{
    [Serializable]
    public sealed class UIPageRegistration
    {
        public string PageId;
        public string ResourceKey;
        public UILayer Layer = UILayer.Normal;
        public bool Cacheable;
        public bool Singleton = true;
        public UIPageDef ToDefinition() => new UIPageDef(PageId, null, new AssetKey(ResourceKey), Layer, Cacheable, Singleton);
    }

    [CreateAssetMenu(menuName = "Yato/UI Page Catalog")]
    public sealed class UIPageCatalog : ScriptableObject, IProjectValidationSource
    {
        public bool RequireResourcesPrefabs = true;
        public UIPageRegistration[] Pages = Array.Empty<UIPageRegistration>();
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var page in Pages ?? Array.Empty<UIPageRegistration>())
            {
                if (page == null) { errors.Add("Null page registration"); continue; }
                if (string.IsNullOrWhiteSpace(page.PageId)) errors.Add("Empty page ID");
                else if (!ids.Add(page.PageId)) errors.Add("Duplicate page ID: " + page.PageId);
                if (!Enum.IsDefined(typeof(UILayer), page.Layer)) errors.Add(page.PageId + ": invalid layer");
                try { if (!new AssetKey(page.ResourceKey).IsValid) errors.Add(page.PageId + ": empty resource key"); }
                catch (Exception error) { errors.Add(page.PageId + ": " + error.Message); }
            }
            return errors.AsReadOnly();
        }
        public UIPageDef Get(string pageId)
        {
            var errors = Validate();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            foreach (var page in Pages ?? Array.Empty<UIPageRegistration>())
                if (page.PageId == pageId) return page.ToDefinition();
            throw new KeyNotFoundException("Unknown UI page: " + pageId);
        }
    }
}
