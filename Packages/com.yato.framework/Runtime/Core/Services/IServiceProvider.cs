namespace Yato.Core
{
    /// <summary>
    /// 轻量服务定位容器，用于替代散落的静态单例。
    /// 注意：与 <see cref="System.IServiceProvider"/> 同名，跨命名空间使用时请显式限定
    /// <c>Yato.Core.IServiceProvider</c>。
    /// </summary>
    public interface IServiceProvider
    {
        /// <summary>以 <typeparamref name="T"/> 为 key 注册服务（后注册覆盖先注册）。</summary>
        void Register<T>(T service);

        /// <summary>解析服务；未注册时抛 <see cref="System.InvalidOperationException"/>。</summary>
        T Resolve<T>();

        /// <summary>尝试解析服务。</summary>
        bool TryResolve<T>(out T service);
    }
}
