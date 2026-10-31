using System;
using System.Collections.Generic;
using Yato.Resources;

namespace Yato.App
{
    /// <summary>
    /// 场景服务（D03）：Single/Additive 加载、显式卸载、公用的互斥执行通道。
    /// <para>忙时立即返回 <see cref="SceneLoadStatus.Busy"/>，不排队。</para>
    /// <para>通过事件驱动 Loading UI，不依赖专门的 Loading Scene。</para>
    /// </summary>
    public interface ISceneService
    {
        /// <summary>是否有场景操作正在进行（加载/等待 Ready/卸载）。</summary>
        bool IsBusy { get; }

        /// <summary>最近一次成功加载并激活的场景上下文。</summary>
        SceneContext ActiveContext { get; }

        /// <summary>当前管理的全部已加载场景（含 Additive）。</summary>
        IReadOnlyList<SceneContext> LoadedContexts { get; }

        /// <summary>最近一次操作结果（含 Busy/Canceled 等拒绝原因）。</summary>
        SceneLoadResult LastResult { get; }

        /// <summary>接受请求并开始加载时触发（用于显示 Loading UI）。</summary>
        event Action<SceneLoadRequest> LoadStarted;

        /// <summary>操作结束（含 Busy/取消/失败）时触发。</summary>
        event Action<SceneLoadResult> LoadCompleted;

        /// <summary>Unity 场景加载完成、尚未 Ready 时触发。</summary>
        event Action<SceneContext> ContextLoaded;

        /// <summary>场景初始化器完成、进入 Ready 时触发。</summary>
        event Action<SceneContext> ContextReady;

        /// <summary>场景被卸载时触发（资源已释放）。</summary>
        event Action<SceneContext> ContextUnloaded;

        IAwaitable<SceneLoadResult> LoadAsync(SceneLoadRequest request);

        IAwaitable<SceneLoadResult> UnloadAsync(SceneContext context);

        /// <summary>释放全部场景上下文与资源；可重复调用。</summary>
        void Shutdown();
    }
}
