using System;
using System.Collections.Generic;
using UnityEngine;
using Yato.Core;

namespace Yato.App
{
    /// <summary>
    /// 全局启动器（文档 D02）：
    /// 注册并按顺序初始化全局服务，关闭时按反向顺序释放；重复调用幂等。
    /// <para>正式构建从专用 Bootstrap Scene 启动，由场景中的本组件驱动。</para>
    /// <para>编辑器下允许从任意场景直接 Play：场景内没有本组件时会自动补初始化（Editor-only）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        private const string HostObjectName = "[Yato.App]";

        /// <summary>已注册（尚未初始化）的服务。</summary>
        private static readonly List<IAppService> s_registered = new List<IAppService>();

        /// <summary>已完成初始化、按初始化顺序记录的服务。</summary>
        private static readonly List<IAppService> s_started = new List<IAppService>();

        private static GameBootstrap s_instance;
        private static AppBootstrapConfig s_config;
        private static ISceneService s_scene;
        private static ResourceScope s_globalResources;
        private static bool s_initialized;
        private static bool s_initializing;

        [SerializeField] private AppBootstrapConfig m_config;

        /// <summary>是否已完成初始化。</summary>
        public static bool IsInitialized
        {
            get { return s_initialized; }
        }

        /// <summary>Bootstrap 根对象（DDOL）。</summary>
        public static GameBootstrap Instance
        {
            get { return s_instance; }
        }

        /// <summary>当前配置（可能为空）。</summary>
        public static AppBootstrapConfig Config
        {
            get { return s_config; }
        }

        /// <summary>全局场景服务；业务通过它发起场景切换。</summary>
        public static ISceneService Scene
        {
            get { return s_scene; }
        }

        /// <summary>全局资源所有权范围：跟随应用生命周期，关闭时统一释放。</summary>
        public static ResourceScope GlobalResources
        {
            get { return s_globalResources; }
        }

        /// <summary>最近一次初始化失败的原因（成功后会被清空）。</summary>
        public static Exception LastInitializationError { get; private set; }

        /// <summary>初始化完成事件；重复初始化不会重复触发。</summary>
        public static event Action Initialized;

        /// <summary>关闭完成事件。</summary>
        public static event Action ShutdownCompleted;

        /// <summary>启动前注入配置（可重复调用，后者覆盖）。</summary>
        public static void Configure(AppBootstrapConfig config)
        {
            if (config != null)
            {
                s_config = config;
            }
        }

        /// <summary>
        /// 注册全局服务。必须在 <see cref="Initialize"/> 之前调用；
        /// 同一具体类型只允许注册一次（重复注册会被忽略并告警）。
        /// </summary>
        public static void RegisterService(IAppService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException("service");
            }

            if (s_initialized)
            {
                Log.Warn(string.Format(
                    "[Yato.App] 已初始化，忽略服务注册：{0}（请在 Bootstrap 之前注册）", service.Name));
                return;
            }

            Type type = service.GetType();
            for (int i = 0; i < s_registered.Count; i++)
            {
                if (s_registered[i].GetType() == type)
                {
                    Log.Warn(string.Format("[Yato.App] 服务重复注册，已忽略：{0}", type.FullName));
                    return;
                }
            }

            s_registered.Add(service);
        }

        /// <summary>
        /// 初始化全部全局服务；重复调用直接返回（幂等）。
        /// 失败时会回滚已完成的服务并抛出 <see cref="AppInitializationException"/>。
        /// </summary>
        public static void Initialize(AppBootstrapConfig config = null)
        {
            if (s_initialized || s_initializing)
            {
                return;
            }

            s_initializing = true;
            try
            {
                if (config != null)
                {
                    s_config = config;
                }

                LastInitializationError = null;

                EnsureHost(s_instance);
                EnsureRuntimeHost();

                if (s_globalResources == null)
                {
                    s_globalResources = new ResourceScope("global");
                }

                EnsureSceneService();

                List<IAppService> ordered = BuildOrderedList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    IAppService service = ordered[i];
                    try
                    {
                        service.Initialize();
                    }
                    catch (Exception e)
                    {
                        // 失败的服务也可能已占用资源：尽力回收，不能让半初始化状态常驻
                        try
                        {
                            service.Shutdown();
                        }
                        catch (Exception cleanup)
                        {
                            Log.Error("GameBootstrap", string.Format("清理失败服务时出错：{0}", service.Name), cleanup);
                        }

                        throw new AppInitializationException(
                            string.Format("[Yato.App] 服务初始化失败：{0}", service.Name),
                            service.Name,
                            e);
                    }

                    s_started.Add(service);
                }

                s_initialized = true;
                Application.quitting += OnApplicationQuitting;
                RaiseInitialized();
            }
            catch (Exception e)
            {
                LastInitializationError = e;
                Rollback();
                throw;
            }
            finally
            {
                s_initializing = false;
            }
        }

        /// <summary>按反向顺序关闭全部服务；重复调用安全。</summary>
        public static void Shutdown()
        {
            if (!s_initialized && s_started.Count == 0)
            {
                return;
            }

            Application.quitting -= OnApplicationQuitting;

            for (int i = s_started.Count - 1; i >= 0; i--)
            {
                IAppService service = s_started[i];
                try
                {
                    service.Shutdown();
                }
                catch (Exception e)
                {
                    // 单个服务关闭失败不能阻断其余服务的释放
                    Log.Error("GameBootstrap", string.Format("服务关闭失败：{0}", service.Name), e);
                }
            }

            s_started.Clear();

            if (s_globalResources != null)
            {
                s_globalResources.ReleaseAll();
                s_globalResources = null;
            }

            DetachSceneService();

            s_initialized = false;

            if (ShutdownCompleted != null)
            {
                try
                {
                    ShutdownCompleted();
                }
                catch (Exception e)
                {
                    Log.Error("GameBootstrap", "ShutdownCompleted 回调异常", e);
                }
            }
        }

        /// <summary>
        /// 仅供测试：清空全部静态状态并销毁 Bootstrap 根对象。
        /// 正常业务不要调用（它会跳过服务的收尾顺序）。
        /// </summary>
        public static void ResetForTests()
        {
            Shutdown();
            s_registered.Clear();
            s_started.Clear();
            s_initialized = false;
            s_initializing = false;
            s_config = null;
            s_scene = null;
            s_globalResources = null;
            LastInitializationError = null;
            Initialized = null;
            ShutdownCompleted = null;
            Application.quitting -= OnApplicationQuitting;

            if (s_instance != null)
            {
                Destroy(s_instance.gameObject);
                s_instance = null;
            }
        }

        /// <summary>已初始化的服务（诊断用，顺序与初始化顺序一致）。</summary>
        public static IReadOnlyList<IAppService> StartedServices
        {
            get { return s_started; }
        }

        private static void EnsureRuntimeHost()
        {
            // RuntimeHost 自身是 DDOL 单例，首次访问会自动创建；协程与每帧驱动都依赖它
            if (!RuntimeHost.IsAlive && RuntimeHost.Instance == null)
            {
                throw new InvalidOperationException("[Yato.App] RuntimeHost 创建失败");
            }
        }

        private static void EnsureHost(GameBootstrap component)
        {
            if (component != null)
            {
                s_instance = component;
                DontDestroyOnLoad(component.gameObject);
                return;
            }

            if (s_instance != null)
            {
                DontDestroyOnLoad(s_instance.gameObject);
                return;
            }

            var host = new GameObject(HostObjectName);
            DontDestroyOnLoad(host);

            // AddComponent 会同步触发 Awake；上面的 s_initializing 标记保证不会递归初始化
            s_instance = host.AddComponent<GameBootstrap>();
        }

        private static void EnsureSceneService()
        {
            if (s_scene != null)
            {
                return;
            }

            // 业务若自行注册了 ISceneService，则复用它的实现
            for (int i = 0; i < s_registered.Count; i++)
            {
                ISceneService custom = s_registered[i] as ISceneService;
                if (custom != null)
                {
                    s_scene = custom;
                    ServiceLocator.Default.Register<ISceneService>(custom);
                    return;
                }
            }

            float timeout = s_config != null
                ? s_config.DefaultSceneReadyTimeoutSeconds
                : SceneService.DefaultReadyTimeoutSeconds;

            var owned = new SceneService(timeout);
            s_scene = owned;
            s_registered.Add(owned);
            ServiceLocator.Default.Register<ISceneService>(owned);
        }

        private static void DetachSceneService()
        {
            if (s_scene != null)
            {
                ServiceLocator.Default.Unregister<ISceneService>();
            }

            // 保留注册列表中的服务实例：重新初始化时复用原 SceneService，
            // 让尚未完成的旧 Unity 操作继续占用同一个互斥通道。
            s_scene = null;
        }

        private static List<IAppService> BuildOrderedList()
        {
            var ordered = new List<IAppService>(s_registered);
            ordered.Sort(delegate(IAppService left, IAppService right)
            {
                int compare = left.Order.CompareTo(right.Order);
                if (compare != 0)
                {
                    return compare;
                }

                return string.CompareOrdinal(left.Name, right.Name);
            });
            return ordered;
        }

        private static void Rollback()
        {
            for (int i = s_started.Count - 1; i >= 0; i--)
            {
                IAppService service = s_started[i];
                try
                {
                    service.Shutdown();
                }
                catch (Exception e)
                {
                    Log.Error("GameBootstrap", string.Format("回滚服务失败：{0}", service.Name), e);
                }
            }

            s_started.Clear();

            if (s_globalResources != null)
            {
                s_globalResources.ReleaseAll();
                s_globalResources = null;
            }

            DetachSceneService();

            Application.quitting -= OnApplicationQuitting;
            s_initialized = false;
        }

        private static void OnApplicationQuitting()
        {
            try
            {
                Shutdown();
            }
            catch (Exception e)
            {
                Log.Error("GameBootstrap", "退出时关闭服务失败", e);
            }
        }

        private static void RaiseInitialized()
        {
            if (Initialized == null)
            {
                return;
            }

            try
            {
                Initialized();
            }
            catch (Exception e)
            {
                Log.Error("GameBootstrap", "Initialized 回调异常", e);
            }
        }

        /// <summary>关闭 Domain Reload / 重新进入 Play 时重置静态状态，避免残留订阅。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Application.quitting -= OnApplicationQuitting;

            s_instance = null;
            s_config = null;
            s_scene = null;
            s_globalResources = null;
            s_initialized = false;
            s_initializing = false;
            LastInitializationError = null;
            s_registered.Clear();
            s_started.Clear();
            Initialized = null;
            ShutdownCompleted = null;
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Log.Warn("[Yato.App] 场景中存在重复的 GameBootstrap，已销毁重复对象");
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);

            try
            {
                Initialize(m_config);
            }
            catch (Exception e)
            {
                // Awake 内不再向上抛，避免打断场景加载；错误已记录在 LastInitializationError
                Log.Error("GameBootstrap", "自动初始化失败", e);
            }
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器下从任意场景直接 Play 时补初始化：
        /// 复用与 Bootstrap Scene 完全相同的配置读取与启动路径（Editor-only，构建中不生效）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EditorAutoInitialize()
        {
            if (s_initialized || s_initializing)
            {
                return;
            }

            // 场景里已有 Bootstrap：交给它自己的 Awake
            if (UnityEngine.Object.FindAnyObjectByType<GameBootstrap>() != null)
            {
                return;
            }

            // 必须全限定：本文件位于 Yato.App 命名空间，名字查找会先命中兄弟命名空间 Yato.Resources，
            // 裸写 Resources 会被解析成命名空间而不是 UnityEngine.Resources（CS0234）。
            AppBootstrapConfig config = UnityEngine.Resources.Load<AppBootstrapConfig>(
                AppBootstrapConfig.DefaultResourcePath);
            if (config != null && !config.AutoInitializeInEditor)
            {
                return;
            }

            try
            {
                Initialize(config);
                Log.Info("[Yato.App] 编辑器自动初始化完成（未使用 Bootstrap Scene）");
            }
            catch (Exception e)
            {
                Log.Error("GameBootstrap", "编辑器自动初始化失败", e);
            }
        }
#endif
    }
}
