using System.Threading;
using Yato.Resources;

namespace Yato.App
{
    /// <summary>
    /// 可选场景初始化器：场景加载完成后运行，完成后该场景才进入 Ready。
    /// <para>必须协作取消：超时后框架会 Cancel 传入的令牌，并等待任务真正结束才释放场景通道。</para>
    /// </summary>
    public interface ISceneInitializer
    {
        IAwaitable InitializeAsync(SceneContext context, CancellationToken cancellationToken);
    }
}
