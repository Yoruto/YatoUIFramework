using System;
using System.Collections.Generic;
using UnityEngine;
namespace Yato.Config
{
    [CreateAssetMenu(menuName = "Yato/Config Catalog")]
    public sealed class ConfigCatalog : ScriptableObject
    {
        public ConfigTableBase[] Tables = Array.Empty<ConfigTableBase>();
        public IReadOnlyList<string> Validate()
        {
            try { using (var service = new ConfigService(this)) return Array.Empty<string>(); }
            catch (ConfigValidationException error) { return error.Errors; }
        }
    }
}
