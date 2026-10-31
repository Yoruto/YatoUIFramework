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

        public T Current
        {
            get { return m_current; }
        }

        public bool IsLoaded
        {
            get { return m_loaded; }
        }

        public SettingsLoadResult LastLoadResult
        {
            get { return m_lastLoadResult; }
        }

        public string RelativePath
        {
            get { return m_relativePath; }
        }

        public event Action<T> Changed;

        public SettingsLoadResult Load()
        {
            bool exists;
            try
            {
                exists = m_store.Exists(m_relativePath);
            }
            catch (Exception e)
            {
                return FallbackToDefaults(SettingsLoadStatus.Failed, e, "检查设置文件失败，使用默认值");
            }

            if (!exists)
            {
                return Publish(SettingsLoadStatus.DefaultsUsed, null, "用户设置文件不存在，使用默认值");
            }

            string text;
            try
            {
                text = m_store.ReadAllText(m_relativePath);
            }
            catch (Exception e)
            {
                return FallbackToDefaults(SettingsLoadStatus.Failed, e, "读取设置文件失败，使用默认值");
            }

            return ApplyLoadedText(text);
        }

        public async Task<SettingsLoadResult> LoadAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool exists;
            try
            {
                exists = m_store.Exists(m_relativePath);
            }
            catch (Exception e)
            {
                return FallbackToDefaults(SettingsLoadStatus.Failed, e, "检查设置文件失败，使用默认值");
            }

            if (!exists)
            {
                return Publish(SettingsLoadStatus.DefaultsUsed, null, "用户设置文件不存在，使用默认值");
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
            catch (Exception e)
            {
                return FallbackToDefaults(SettingsLoadStatus.Failed, e, "读取设置文件失败，使用默认值");
            }

            return ApplyLoadedText(text);
        }

        public void Save()
        {
            Validate(m_current);
            m_store.WriteAllText(m_relativePath, m_serializer.Serialize(m_current));
        }

        public async Task SaveAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Validate(m_current);
            string json = m_serializer.Serialize(m_current);
            await m_store.WriteAllTextAsync(m_relativePath, json, cancellationToken);
        }

        public void Apply(Action<T> mutate)
        {
            if (mutate == null)
            {
                throw new ArgumentNullException("mutate");
            }

            mutate(m_current);
            RaiseChanged();
        }

        public T ResetToDefaults()
        {
            m_current = Clone(m_defaultsTemplate);
            RaiseChanged();
            return m_current;
        }

        public T CreateDefaultsSnapshot()
        {
            return Clone(m_defaultsTemplate);
        }

        private SettingsLoadResult ApplyLoadedText(string text)
        {
            T user;
            try
            {
                user = m_serializer.Deserialize<T>(text);
            }
            catch (Exception e)
            {
                return FallbackToDefaults(SettingsLoadStatus.Corrupted, e, "设置文件内容损坏，已保留原文件并使用默认值");
            }

            try
            {
                Validate(user);
            }
            catch (Exception e)
            {
                return FallbackToDefaults(SettingsLoadStatus.Invalid, e, "设置内容未通过校验，已保留原文件并使用默认值");
            }

            m_current = user;
            return Publish(SettingsLoadStatus.Loaded, null, null);
        }

        /// <summary>回退到默认值，但绝不写盘（D05：不自动覆盖损坏文件）。</summary>
        private SettingsLoadResult FallbackToDefaults(SettingsLoadStatus status, Exception error, string message)
        {
            m_current = Clone(m_defaultsTemplate);
            return Publish(status, error, message);
        }

        private SettingsLoadResult Publish(SettingsLoadStatus status, Exception error, string message)
        {
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

            // 逐个隔离：单个订阅者异常不能影响其它订阅者
            Delegate[] invocationList = handlers.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                var handler = (Action<T>)invocationList[i];
                try
                {
                    handler(m_current);
                }
                catch (Exception e)
                {
                    Log.Error("SettingsService", "Changed 回调异常：" + handler.Method.Name, e);
                }
            }
        }
    }
}
