namespace Yato.App
{
    /// <summary>
    /// 由 <see cref="GameBootstrap"/> 管理的全局服务（文档 D02）。
    /// <para>初始化按 <see cref="Order"/> 升序执行，关闭按反向顺序释放。</para>
    /// <para>失败时框架会尽力回收已经完成初始化的服务，不会留下半初始化状态。</para>
    /// </summary>
    public interface IAppService
    {
        /// <summary>诊断名。</summary>
        string Name { get; }

        /// <summary>初始化顺序，越小越先初始化（默认 0）。</summary>
        int Order { get; }

        /// <summary>注册服务实例、建立内部状态；允许依赖更早初始化的服务。</summary>
        void Initialize();

        /// <summary>释放该服务持有的全部资源与订阅；必须可重复调用。</summary>
        void Shutdown();
    }
}
