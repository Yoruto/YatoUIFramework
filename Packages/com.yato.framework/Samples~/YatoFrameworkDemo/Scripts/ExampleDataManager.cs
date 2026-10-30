namespace Yato.FrameworkDemo
{
    /// <summary>
    /// 示例数据源。框架不再提供静态单例（旧 Singleton&lt;T&gt; 已删除），
    /// 业务侧可通过 Yato.Core.ServiceLocator 注册后注入。
    /// </summary>
    public static class ExampleDataManager
    {
        private static int s_value;

        public static int Value
        {
            get { return s_value; }
        }

        public static int Increment()
        {
            s_value++;
            return s_value;
        }

        public static void Reset()
        {
            s_value = 0;
        }
    }
}
