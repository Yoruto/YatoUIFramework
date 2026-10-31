using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yato.Persistence;

namespace Yato.Save
{
    public enum SaveLoadStatus { Missing, Loaded, Migrated, Corrupt, Incompatible, Failed }
    public sealed class SaveLoadResult<T>
    {
        public SaveLoadStatus Status { get; internal set; }
        public T Data { get; internal set; }
        public int DataVersion { get; internal set; }
        public string SavedAtUtc { get; internal set; }
        public bool BackupAvailable { get; internal set; }
        public Exception Error { get; internal set; }
    }
    public sealed class SaveSlotInfo
    {
        public string SlotId { get; internal set; }
        public SaveLoadStatus Status { get; internal set; }
        public int DataVersion { get; internal set; }
        public string SavedAtUtc { get; internal set; }
        public bool BackupAvailable { get; internal set; }
    }
    public interface ISaveMigrator<T>
    {
        T Migrate(int sourceVersion, int targetVersion, string payloadJson);
    }
    public interface ISaveService<T> where T : class
    {
        Task<SaveLoadResult<T>> LoadAsync(string slotId, CancellationToken ct = default);
        Task SaveAsync(string slotId, T data, CancellationToken ct = default);
        Task<SaveLoadResult<T>> RecoverBackupAsync(string slotId, CancellationToken ct = default);
        Task<IReadOnlyList<SaveSlotInfo>> ListAsync(CancellationToken ct = default);
        Task DeleteAsync(string slotId, CancellationToken ct = default);
    }
    [Serializable]
    internal sealed class SaveEnvelope
    {
        public int formatVersion;
        public int dataVersion;
        public string savedAtUtc;
        public string payload;
    }
    internal static class SaveSlotGate
    {
        private static readonly HashSet<string> Active = new HashSet<string>(Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        internal static IDisposable Acquire(string path)
        {
            lock (Active) if (!Active.Add(path)) throw new FileBusyException(path);
            return new Lease(path);
        }
        private sealed class Lease : IDisposable
        {
            private string m_path;
            public Lease(string path) { m_path = path; }
            public void Dispose() { lock (Active) { if (m_path != null) Active.Remove(m_path); m_path = null; } }
        }
    }

    /// <summary>Optional save policy above IFileStore. Corrupt files are never silently replaced or recovered.</summary>
    public sealed class SaveService<T> : ISaveService<T> where T : class
    {
        private readonly IFileStore m_store;
        private readonly ISerializer m_serializer;
        private readonly ISaveMigrator<T> m_migrator;
        private readonly Func<T, string> m_validate;
        private readonly string m_directory;
        private readonly int m_version;

        public SaveService(IFileStore store, int dataVersion, Func<T, string> validate = null,
            ISaveMigrator<T> migrator = null, string directory = "saves", ISerializer serializer = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            if (dataVersion < 1) throw new ArgumentOutOfRangeException(nameof(dataVersion));
            m_directory = PersistencePaths.Normalize(directory);
            m_version = dataVersion; m_validate = validate; m_migrator = migrator;
            m_serializer = serializer ?? new JsonUtilitySerializer();
        }

        private string SlotDirectory(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64 ||
                id.Any(c => !(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '-'))
                throw new ArgumentException("Slot ID must contain 1-64 ASCII letters, digits, '_' or '-'", nameof(id));
            return m_directory + "/slot-" + id;
        }
        private IDisposable Acquire(string id) => SaveSlotGate.Acquire(Path.GetFullPath(Path.Combine(m_store.RootPath, SlotDirectory(id))));
        private string Primary(string id) => SlotDirectory(id) + "/primary.json";
        private string Backup(string id) => SlotDirectory(id) + "/backup.json";
        private void Validate(T data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            string error = m_validate?.Invoke(data);
            if (!string.IsNullOrEmpty(error)) throw new InvalidDataException(error);
        }
        private string Encode(T data)
        {
            Validate(data);
            return m_serializer.Serialize(new SaveEnvelope { formatVersion = 1, dataVersion = m_version,
                savedAtUtc = DateTime.UtcNow.ToString("O"), payload = m_serializer.Serialize(data) });
        }
        private SaveLoadResult<T> Decode(string text)
        {
            var result = new SaveLoadResult<T>();
            try
            {
                var envelope = m_serializer.Deserialize<SaveEnvelope>(text);
                result.DataVersion = envelope.dataVersion; result.SavedAtUtc = envelope.savedAtUtc;
                if (envelope.formatVersion != 1 || envelope.dataVersion < 1 || string.IsNullOrEmpty(envelope.payload) ||
                    !DateTime.TryParse(envelope.savedAtUtc, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out _))
                    throw new InvalidDataException("Invalid save envelope");
                if (envelope.dataVersion > m_version || (envelope.dataVersion < m_version && m_migrator == null))
                {
                    result.Status = SaveLoadStatus.Incompatible;
                    result.Error = new InvalidDataException("Unsupported save data version: " + envelope.dataVersion);
                    return result;
                }
                result.Data = envelope.dataVersion == m_version ? m_serializer.Deserialize<T>(envelope.payload)
                    : m_migrator.Migrate(envelope.dataVersion, m_version, envelope.payload);
                Validate(result.Data);
                result.Status = envelope.dataVersion == m_version ? SaveLoadStatus.Loaded : SaveLoadStatus.Migrated;
            }
            catch (Exception error) { result.Status = SaveLoadStatus.Corrupt; result.Data = null; result.Error = error; }
            return result;
        }
        private async Task<SaveLoadResult<T>> Read(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!m_store.Exists(path)) return new SaveLoadResult<T> { Status = SaveLoadStatus.Missing };
                return Decode(await m_store.ReadAllTextAsync(path, ct));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { return new SaveLoadResult<T> { Status = SaveLoadStatus.Failed, Error = error }; }
        }
        private static bool Valid(SaveLoadResult<T> result) => result.Status == SaveLoadStatus.Loaded || result.Status == SaveLoadStatus.Migrated;

        public async Task<SaveLoadResult<T>> LoadAsync(string slotId, CancellationToken ct = default)
        {
            using (Acquire(slotId))
            {
                var primary = await Read(Primary(slotId), ct);
                primary.BackupAvailable = Valid(await Read(Backup(slotId), ct));
                return primary;
            }
        }

        public async Task SaveAsync(string slotId, T data, CancellationToken ct = default)
        {
            using (Acquire(slotId))
            {
                ct.ThrowIfCancellationRequested();
                string next = Encode(data); // Snapshot caller data before the first asynchronous boundary.
                string primaryPath = Primary(slotId);
                if (m_store.Exists(primaryPath))
                {
                    string old = await m_store.ReadAllTextAsync(primaryPath, ct);
                    var previous = Decode(old);
                    if (!Valid(previous)) throw new InvalidDataException("Existing save is unreadable; explicitly recover or delete it before saving", previous.Error);
                    await m_store.WriteAllTextAsync(Backup(slotId), old, ct);
                }
                await m_store.WriteAllTextAsync(primaryPath, next, ct); // Do not check cancellation after atomic commit.
            }
        }

        public async Task<SaveLoadResult<T>> RecoverBackupAsync(string slotId, CancellationToken ct = default)
        {
            using (Acquire(slotId))
            {
                var backup = await Read(Backup(slotId), ct);
                if (!Valid(backup)) return backup;
                await m_store.WriteAllTextAsync(Primary(slotId), Encode(backup.Data), ct);
                backup.BackupAvailable = true;
                return backup;
            }
        }

        public async Task<IReadOnlyList<SaveSlotInfo>> ListAsync(CancellationToken ct = default)
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string file in m_store.EnumerateFiles(m_directory, "*.json", true))
            {
                string relative = file.Replace('\\', '/').Substring(m_directory.Length).TrimStart('/');
                string[] parts = relative.Split('/');
                if (parts.Length == 2 && parts[0].StartsWith("slot-", StringComparison.Ordinal) &&
                    (parts[1] == "primary.json" || parts[1] == "backup.json"))
                {
                    string id = parts[0].Substring(5);
                    try { SlotDirectory(id); ids.Add(id); } catch (ArgumentException) { }
                }
            }
            var results = new List<SaveSlotInfo>();
            foreach (string id in ids)
            {
                ct.ThrowIfCancellationRequested();
                var loaded = await LoadAsync(id, ct);
                results.Add(new SaveSlotInfo { SlotId = id, Status = loaded.Status, DataVersion = loaded.DataVersion,
                    SavedAtUtc = loaded.SavedAtUtc, BackupAvailable = loaded.BackupAvailable });
            }
            return results.AsReadOnly();
        }

        public Task DeleteAsync(string slotId, CancellationToken ct = default)
        {
            using (Acquire(slotId))
            {
                ct.ThrowIfCancellationRequested();
                m_store.Delete(Primary(slotId));
                m_store.Delete(Backup(slotId));
            }
            return Task.CompletedTask; // File deletion is explicit and not a multi-file atomic transaction.
        }
    }
}
