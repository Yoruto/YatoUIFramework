using System;
using System.Threading;
using UnityEngine.SceneManagement;

namespace Yato.App
{
    /// <summary>一次场景加载请求。</summary>
    public sealed class SceneLoadRequest
    {
        public SceneLoadRequest(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            SceneName = sceneName;
            Mode = mode;
        }

        /// <summary>场景名或工程内路径（需在 Build Settings 中）。</summary>
        public string SceneName { get; set; }

        /// <summary>默认 <see cref="LoadSceneMode.Single"/>；Additive 需显式指定。</summary>
        public LoadSceneMode Mode { get; set; }

        /// <summary>可选的场景初始化器：完成后场景才进入 Ready。</summary>
        public ISceneInitializer Initializer { get; set; }

        /// <summary>Ready 超时（秒）；&lt;= 0 表示使用服务默认值（60s）。</summary>
        public float ReadyTimeoutSeconds { get; set; }

        /// <summary>取消令牌：只在 Unity 加载真正开始前生效。</summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>加载进度回调（0..1）。</summary>
        public IProgress<float> Progress { get; set; }

        /// <summary>Single 加载完成后是否设置为活动场景。</summary>
        public bool ActivateOnLoad { get; set; } = true;

        public SceneLoadRequest WithInitializer(ISceneInitializer initializer)
        {
            Initializer = initializer;
            return this;
        }

        public SceneLoadRequest WithReadyTimeout(float seconds)
        {
            ReadyTimeoutSeconds = seconds;
            return this;
        }

        public SceneLoadRequest WithCancellation(CancellationToken token)
        {
            CancellationToken = token;
            return this;
        }

        public SceneLoadRequest WithProgress(IProgress<float> progress)
        {
            Progress = progress;
            return this;
        }

        public SceneLoadRequest WithoutActivation()
        {
            ActivateOnLoad = false;
            return this;
        }

        public override string ToString()
        {
            return string.Format("{0}({1})", SceneName, Mode);
        }
    }
}
