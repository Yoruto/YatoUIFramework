using System;
using System.Threading;
using System.Threading.Tasks;
using Yato.Core;
using Yato.Persistence;

namespace Yato.Settings
{
    /// <summary>
    /// <see cref="ISettingsService{T}"/> 默认实现（D05）。
    /// <para>默认值只作为模板保存，运行期始终操作副本，不会覆盖原始默认资源。</para>
    /// <para>加载顺序：默认值 → 尝试用户文件 → 游戏自定义校验 → 发布。</para>
    /// <para>文件缺失保留默认值；损坏/非法时保留原文件、明确报告，运行时暂用默认值但不写盘。</para>
    /// </summary>
    public sealed class SettingsService<T> : ISettingsService<T> where T : class
    {
        private readonly IFileStore m_store;
        private readonly ISerializer m_serializer;
        private readonly Action<T> m_validator;
        private readonly string m_relativePath;
        private readonly object m_stateGate = new object();
        private readonly SemaphoreSlim m_loadGate = new SemaphoreSlim(1, 1);
        private long m_revision;

        /// <summary>默认值模板：只读，永不对外暴露。</summary>
        private readonly T m_defaultsTemplate;

        private T m_current;
        private bool m_loaded;
        private SettingsLoadResult m_lastLoadResult;

        public SettingsService(
            T defaults,
            IFileStore store,
            string relativePath,
            ISerializer serializer = null,
            Action<T> validator = null)
        {
            if (defaults == null)
            {
                throw new ArgumentNullException("defaults");
            }

            if (store == null)
            {
                throw new ArgumentNullException("store");
            }

            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException("设置文件相对路径不能为空", "relativePath");
            }

            m_store = store;
            m_serializer = serializer ?? new JsonUtilitySerializer();
            m_validator = validator;
            m_relativePath = PersistencePaths.Normalize(relativePath);

            // 通过序列化做深拷贝：同时验证默认值可被当前序列化器处理
            m_defaultsTemplate = Clone(defaults);
            m_current = Clone(defaults);
        }

        /// <summary>
        /// 当前设置的快照：每次读取都返回副本，调用方修改它不会影响服务状态，
        /// 也不会绕过 <see cref="Apply"/> 的 Changed 通知与校验。
        /// </summary>
        public T Current
        {
            get { lock (m_stateGate) return Clone(m_current); }
        }

        public bool IsLoaded
        {
            get { lock (m_stateGate) return m_loaded; }
        }

        public SettingsLoadResult LastLoadResult
        {
            get { lock (m_stateGate) return m_lastLoadResult; }
        }

        public string RelativePath
        {
            get { return m_relativePath; }
        }

        public event Action<T> Changed;

        public SettingsLoadResult Load()
        {
            // 不在调用线程阻塞等待异步加载：它的 continuation 可能需要 Unity 主线程。
            if (!m_loadGate.Wait(0)) throw new FileBusyException(m_store.RootPath + "/" + m_relativePath);
            try { return LoadCore(BeginLoad()); }
            finally { m_loadGate.Release(); }
        }

        private SettingsLoadResult LoadCore(long revision)
        {
            bool exists;
            try
            {
                exists = m_store.Exists(m_relativePath);
            }
            catch (FileBusyException) { throw; }
            catch (Exception e)
            {
                return ApplyDefaults(revision, SettingsLoadStatus.Failed, e, "检查设置文件失败，使用默认值");
            }

            if (!exists)
            {
                // 缺文件必须真正回到默认值：重复 Load 时不能沿用上一次（可能已被 Apply 修改）的运行期状态
                return ApplyDefaults(revision, SettingsLoadStatus.DefaultsUsed, null, "用户设置文件不存在，使用默认值");
            }

            string text;
            try
            {
                text = m_store.ReadAllText(m_relativePath);
            }
            catch (FileBusyException) { throw; }
            catch (Exception e)
            {
                return ApplyDefaults(revision, SettingsLoadStatus.Failed, e, "读取设置文件失败，使用默认值");
            }

            return ApplyLoadedText(text, revision);
        }

        public async Task<SettingsLoadResult> LoadAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            long revision = BeginLoad();
            await m_loadGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (m_stateGate)
                    if (revision != m_revision) return Superseded();
                return await LoadCoreAsync(revision, cancellationToken);
            }
            finally { m_loadGate.Release(); }
        }

        private async Task<SettingsLoadResult> LoadCoreAsync(long revision, CancellationToken cancellationToken)
        {
            bool exists;
            try
            {
                exists = m_store.Exists(m_relativePath);
            }
            catch (OperationCanceledException) { throw; }
            catch (FileBusyException) { throw; }
            catch (Exception e)
            {
                return ApplyDefaults(revision, SettingsLoadStatus.Failed, e, "检查设置文件失败，使用默认值");
            }

            if (!exists)
            {
                // 与同步加载保持一致：缺文件时恢复默认值后再发布
                return ApplyDefaults(revision, SettingsLoadStatus.DefaultsUsed, null, "用户设置文件不存在，使用默认值");
            }

            string text;
            try
            {
                text = await m_store.ReadAllTextAsync(m_relativePath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 取消不是加载失败，交给调用方处理
                throw;
            }
            catch (FileBusyException) { throw; }
            catch (Exception e)
            {
                return ApplyDefaults(revision, SettingsLoadStatus.Failed, e, "读取设置文件失败，使用默认值");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ApplyLoadedText(text, revision);
        }

        public void Save()
        {
            T snapshot = Current;
            Validate(snapshot);
            m_store.WriteAllText(m_relativePath, m_serializer.Serialize(snapshot));
        }

        public async Task SaveAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            T snapshot = Current;
            Validate(snapshot);
            string json = m_serializer.Serialize(snapshot);
            await m_store.WriteAllTextAsync(m_relativePath, json, cancellationToken);
        }

        public void Apply(Action<T> mutate)
        {
            if (mutate == null)
            {
                throw new ArgumentNullException("mutate");
            }

            lock (m_stateGate)
            {
                m_revision++;
                mutate(m_current);
                RaiseChanged();
            }
        }

        public T ResetToDefaults()
        {
            lock (m_stateGate)
            {
                m_revision++;
                m_current = Clone(m_defaultsTemplate);
                RaiseChanged();
                return Clone(m_current);
            }
        }

        public T CreateDefaultsSnapshot()
        {
            return Clone(m_defaultsTemplate);
        }

        private SettingsLoadResult ApplyLoadedText(string text, long revision)
        {
            lock (m_stateGate)
                if (revision != m_revision) return Superseded();
            T user;
            try
            {
                user = m_serializer.Deserialize<T>(text);
            }
            catch (Exception e)
            {
                return ApplyDefaults(revision, SettingsLoadStatus.Corrupted, e, "设置文件内容损坏，已保留原文件并使用默认值");
            }

            try
            {
                Validate(user);
            }
            catch (Exception e)
            {
                return ApplyDefaults(revision, SettingsLoadStatus.Invalid, e, "设置内容未通过校验，已保留原文件并使用默认值");
            }

            return Publish(user, revision, SettingsLoadStatus.Loaded, null, null);
        }

        private long BeginLoad()
        {
            lock (m_stateGate) return ++m_revision;
        }

        private static SettingsLoadResult Superseded()
        {
            return SettingsLoadResult.Create(SettingsLoadStatus.Superseded, null, "已有更新的设置操作，本次加载结果未发布");
        }

        /// <summary>回退到默认值，但绝不写盘；过期加载也不能回退当前状态。</summary>
        private SettingsLoadResult ApplyDefaults(long revision, SettingsLoadStatus status, Exception error, string message)
        {
            return Publish(Clone(m_defaultsTemplate), revision, status, error, message);
        }

        private SettingsLoadResult Publish(T value, long revision, SettingsLoadStatus status, Exception error, string message)
        {
            lock (m_stateGate)
            {
                if (revision != m_revision) return Superseded();
                m_current = value;
                m_loaded = true;

                var result = SettingsLoadResult.Create(status, error, message);
                m_lastLoadResult = result;

                if (error != null)
                {
                    Log.Error("SettingsService", message, error);
                }
                else if (status == SettingsLoadStatus.Loaded)
                {
                    Log.Info(string.Format("[Yato.Settings] 已加载用户设置：{0}", m_relativePath));
                }

                RaiseChanged();
                return result;
            }
        }

        private void Validate(T value)
        {
            if (m_validator == null)
            {
                return;
            }

            try
            {
                m_validator(value);
            }
            catch (SettingsValidationException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new SettingsValidationException(
                    string.Format("设置内容未通过校验：{0}", m_relativePath), e);
            }
        }

        private T Clone(T value)
        {
            return m_serializer.Deserialize<T>(m_serializer.Serialize(value));
        }

        private void RaiseChanged()
        {
            Action<T> handlers = Changed;
            if (handlers == null)
            {
                return;
            }

            // 事件参数与 Current 一样是快照：订阅者修改它不会影响服务内部状态
            T snapshot = Clone(m_current);
            long revision = m_revision;

            // 逐个隔离：单个订阅者异常不能影响其它订阅者
            Delegate[] invocationList = handlers.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                // 前面的观察者可能重入 Apply/Reset：后续观察者只应收到更新后的通知。
                if (revision != m_revision) return;
                var handler = (Action<T>)invocationList[i];
                try
                {
                    handler(snapshot);
                }
                catch (Exception e)
                {
                    Log.Error("SettingsService", "Changed 回调异常：" + handler.Method.Name, e);
                }
            }
        }
    }
}
