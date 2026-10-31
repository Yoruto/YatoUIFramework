namespace Yato.App
{
    /// <summary>服务基类：提供默认的名称/顺序与空的关闭实现。</summary>
    public abstract class AppServiceBase : IAppService
    {
        public virtual string Name
        {
            get { return GetType().Name; }
        }

        public virtual int Order
        {
            get { return 0; }
        }

        public abstract void Initialize();

        public virtual void Shutdown()
        {
        }
    }
}
