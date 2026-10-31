using System;

namespace Yato.Core
{
    /// <summary>
    /// 类型化事件总线。订阅返回 <see cref="IDisposable"/>，Dispose 即退订。
    /// </summary>
    public interface IEventBus
    {
        /// <summary>订阅事件；Dispose 返回值即退订。</summary>
        IDisposable Subscribe<T>(Action<T> handler);

        /// <summary>在当前线程派发。监听者异常会被记录并继续派发其余监听者。</summary>
        void Publish<T>(T evt);

        /// <summary>切到主线程派发（网络线程 / 异步回调安全）。</summary>
        void PublishOnMainThread<T>(T evt);

        /// <summary>清空全部订阅（场景切换 / 应用退出）。</summary>
        void Clear();
    }
}
