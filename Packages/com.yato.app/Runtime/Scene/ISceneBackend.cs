using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yato.App
{
    /// <summary>可轮询完成状态的场景操作（避免直接依赖无法伪造的 <see cref="AsyncOperation"/>）。</summary>
    public interface ISceneOperation
    {
        bool IsDone { get; }

        float Progress { get; }
    }

    /// <summary>
    /// 场景后端：默认实现直接调用 Unity <see cref="SceneManager"/>。
    /// <para>
    /// 单独抽出的目的：让 <see cref="SceneService"/> 的并发/取消/就绪/超时语义可以脱离真实场景资源测试，
    /// 同时为 Addressables 等自定义加载方式预留接入口；默认路径仍然是 D03 冻结的直接调用。
    /// </para>
    /// </summary>
    public interface ISceneBackend
    {
        /// <summary>场景是否可加载（默认实现对应 Build Settings）。</summary>
        bool CanLoad(string sceneNameOrPath);

        /// <summary>开始加载（返回后立即开始，由调用方轮询）。</summary>
        ISceneOperation LoadScene(string sceneNameOrPath, LoadSceneMode mode);

        /// <summary>开始卸载。</summary>
        ISceneOperation UnloadScene(Scene scene);

        /// <summary>查找已加载的场景；找不到返回非法 <see cref="Scene"/>。</summary>
        Scene FindLoadedScene(string sceneNameOrPath);

        /// <summary>设置活动场景。</summary>
        bool SetActiveScene(Scene scene);

        /// <summary>当前已加载场景数量。</summary>
        int LoadedSceneCount { get; }
    }
}
