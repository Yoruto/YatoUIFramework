using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Yato.Resources;

namespace Yato.Config
{
    [Serializable]
    public abstract class ConfigRecord
    {
        public string Id;
        public virtual void Validate(ConfigValidationContext context) { }
    }
    public abstract class ConfigTableBase : ScriptableObject
    {
        public abstract Type RecordType { get; }
        public abstract IEnumerable<ConfigRecord> Records { get; }
    }
    public abstract class ConfigTable<T> : ConfigTableBase where T : ConfigRecord
    {
        [SerializeField] private T[] m_records = Array.Empty<T>();
        public T[] Rows { get => m_records; set => m_records = value; }
        public override Type RecordType => typeof(T);
        public override IEnumerable<ConfigRecord> Records => m_records ?? Array.Empty<T>();
    }
    public sealed class ConfigValidationException : Exception
    {
        public IReadOnlyList<string> Errors { get; }
        internal ConfigValidationException(List<string> errors) : base(string.Join("\n", errors)) { Errors = errors.AsReadOnly(); }
    }
    public sealed class ConfigValidationContext
    {
        private readonly ConfigService m_service;
        private readonly List<string> m_errors;
        private readonly string m_owner;
        internal ConfigValidationContext(ConfigService service, List<string> errors, string owner)
        { m_service = service; m_errors = errors; m_owner = owner; }
        public void Report(string message) { m_errors.Add(m_owner + ": " + message); }
        public void Require<T>(string id) where T : ConfigRecord
        { if (!m_service.TryGet<T>(id, out _)) Report("Missing reference " + typeof(T).Name + "/" + id); }
    }

    /// <summary>Runtime copies of configuration rows. Owns its catalog handle until disposed.</summary>
    public sealed class ConfigService : IDisposable
    {
        private readonly Dictionary<Type, Dictionary<string, ConfigRecord>> m_tables = new Dictionary<Type, Dictionary<string, ConfigRecord>>();
        private IAssetHandle<ConfigCatalog> m_handle;
        private bool m_disposed;

        public ConfigService(ConfigCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var errors = new List<string>();
            foreach (var table in catalog.Tables ?? Array.Empty<ConfigTableBase>())
            {
                if (table == null) { errors.Add("Null configuration table"); continue; }
                if (m_tables.ContainsKey(table.RecordType)) { errors.Add("Duplicate table type: " + table.RecordType); continue; }
                var index = new Dictionary<string, ConfigRecord>(StringComparer.Ordinal);
                m_tables.Add(table.RecordType, index);
                foreach (var row in table.Records)
                {
                    if (row == null || string.IsNullOrWhiteSpace(row.Id)) { errors.Add(table.name + ": null row or empty ID"); continue; }
                    if (index.ContainsKey(row.Id)) { errors.Add(table.name + ": duplicate ID " + row.Id); continue; }
                    var copy = (ConfigRecord)JsonUtility.FromJson(JsonUtility.ToJson(row), row.GetType());
                    index.Add(row.Id, copy);
                }
            }
            foreach (var table in m_tables)
                foreach (var row in table.Value.Values)
                    try { row.Validate(new ConfigValidationContext(this, errors, table.Key.Name + "/" + row.Id)); }
                    catch (Exception error) { errors.Add(table.Key.Name + "/" + row.Id + ": " + error.Message); }
            if (errors.Count > 0) throw new ConfigValidationException(errors);
        }

        public static IAwaitable<ConfigService> LoadAsync(IResourceService resources, AssetKey key, CancellationToken ct = default)
        {
            var source = new Yato.Resources.AwaitableCompletionSource<ConfigService>();
            try
            {
                if (resources == null) throw new ArgumentNullException(nameof(resources));
                var operation = resources.LoadAsync<ConfigCatalog>(key, ct);
                operation.Completed += () =>
                {
                    IAssetHandle<ConfigCatalog> handle = null;
                    try
                    {
                        handle = operation.GetResult();
                        ct.ThrowIfCancellationRequested();
                        var service = new ConfigService(handle.Asset) { m_handle = handle };
                        handle = null;
                        source.SetResult(service);
                    }
                    catch (Exception error) { handle?.Dispose(); source.SetException(error); }
                };
            }
            catch (Exception error) { source.SetException(error); }
            return source;
        }

        public bool TryGet<T>(string id, out T row) where T : ConfigRecord
        {
            if (m_disposed) throw new ObjectDisposedException(nameof(ConfigService));
            row = null;
            if (id == null || !m_tables.TryGetValue(typeof(T), out var table) || !table.TryGetValue(id, out var value)) return false;
            row = (T)value;
            return true;
        }
        public T Get<T>(string id) where T : ConfigRecord => TryGet<T>(id, out var row) ? row : throw new KeyNotFoundException(typeof(T).Name + "/" + id);
        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            m_handle?.Dispose(); m_handle = null;
            m_tables.Clear();
        }
    }
}
