using System;

namespace Yato.App
{
    /// <summary>场景初始化器超时；场景保持 Loaded 但不会进入 Ready。</summary>
    public sealed class SceneInitializerTimeoutException : Exception
    {
        public SceneInitializerTimeoutException(string sceneName, float timeoutSeconds)
            : base(string.Format(
                "场景 {0} 的初始化器超时（{1:0.##}s）；为安全起见，在旧任务真正结束前不释放场景互斥通道。",
                sceneName,
                timeoutSeconds))
        {
            SceneName = sceneName;
            TimeoutSeconds = timeoutSeconds;
        }

        public string SceneName { get; private set; }

        public float TimeoutSeconds { get; private set; }
    }
}
