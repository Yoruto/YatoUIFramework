using System;

namespace Yato.Resources
{
    /// <summary>
    /// 异步结果契约。与 <c>Task</c> 的区别是完全由框架驱动，不依赖线程池，
    /// 完成后在主线程回调 <see cref="Completed"/>。
    /// </summary>
    public interface IAwaitable
    {
        bool IsDone { get; }

        event Action Completed;

        /// <summary>取结果；失败时抛 <see cref="ResourceException"/>。未完成时调用属于逻辑错误。</summary>
        void GetResult();
    }

    /// <summary>带返回值的异步结果。</summary>
    public interface IAwaitable<T> : IAwaitable
    {
        new T GetResult();
    }
}
