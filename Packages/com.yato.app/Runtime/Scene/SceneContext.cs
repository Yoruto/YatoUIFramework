using System;
using UnityEngine.SceneManagement;

namespace Yato.App
{
    /// <summary>场景生命周期状态（Loaded 与 Ready 必须区分，见 D03）。</summary>
    public enum SceneLoadState
    {
        None = 0,
        Loading = 1,
        /// <summary>Unity 场景已加载；不保证场景内 MonoBehaviour 的 Start 已执行。</summary>
        Loaded = 2,
        /// <summary>可选初始化器已完成；可以进入玩法。</summary>
        Ready = 3,
        Unloading = 4,
        Unloaded = 5,
        Failed = 6
    }

    /// <summary>
    /// 单个场景拥有的运行上下文：场景句柄、Ready 状态、失败原因与独立的资源范围。
    /// Additive 场景各自拥有独立的 Ready 状态与 <see cref="Resources"/>。
    /// </summary>
    public sealed class SceneContext
    {
        private readonly ResourceScope m_resources;

        public SceneContext(string sceneName, LoadSceneMode mode)
        {
            SceneName = sceneName ?? string.Empty;
            Mode = mode;
            State = SceneLoadState.Loading;
            m_resources = new ResourceScope(SceneName);
        }

        /// <summary>Unity 场景句柄；卸载后仍然可读，但 <see cref="Scene.IsValid"/> 为 false。</summary>
        public Scene Scene { get; internal set; }

        public string SceneName { get; private set; }

        public LoadSceneMode Mode { get; private set; }

        public SceneLoadState State { get; internal set; }

        /// <summary>失败原因（Ready 初始化器失败/超时、卸载失败等）。</summary>
        public Exception Failure { get; internal set; }

        /// <summary>该场景独占的资源范围；卸载时统一释放。</summary>
        public ResourceScope Resources
        {
            get { return m_resources; }
        }

        /// <summary>场景已加载（可能是 Loaded 或 Ready）。</summary>
        public bool IsLoaded
        {
            get { return State == SceneLoadState.Loaded || State == SceneLoadState.Ready; }
        }

        /// <summary>场景初始化器已完成。</summary>
        public bool IsReady
        {
            get { return State == SceneLoadState.Ready; }
        }

        /// <summary>是否仍然可用（未被卸载）。</summary>
        public bool IsAlive
        {
            get { return State != SceneLoadState.Unloaded; }
        }

        internal void MarkLoaded(Scene scene)
        {
            Scene = scene;
            State = SceneLoadState.Loaded;
        }

        internal void MarkReady()
        {
            State = SceneLoadState.Ready;
        }

        internal void MarkFailed(Exception error)
        {
            Failure = error;
            State = SceneLoadState.Failed;
        }

        internal void MarkUnloaded()
        {
            State = SceneLoadState.Unloaded;
            EffectivelyReleaseResources();
        }

        private void EffectivelyReleaseResources()
        {
            try
            {
                m_resources.ReleaseAll();
            }
            catch (Exception)
            {
                // ReleaseAll 内部已隔离单个句柄异常，这里只兜底
            }
        }

        public override string ToString()
        {
            return string.Format("{0}({1}, {2})", SceneName, Mode, State);
        }
    }
}
