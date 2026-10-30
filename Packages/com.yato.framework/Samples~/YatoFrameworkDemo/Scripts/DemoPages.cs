using Yato.Resources;
using Yato.UI;

namespace Yato.FrameworkDemo
{
    /// <summary>
    /// 页面表。P4 的 AssetKeyGenerator 会从预制体目录自动生成 UIPages.g.cs，
    /// 这里先手写一份等价的示例。
    /// </summary>
    public static class DemoPages
    {
        public static readonly UIPageDef Example = new UIPageDef(
            pageId: "Example",
            mediatorType: typeof(ExampleMediator),
            prefabKey: new AssetKey("ui/example/example"),
            layer: UILayer.Normal,
            cacheable: true,
            singleton: true);

        public static readonly UIPageDef Confirm = new UIPageDef(
            pageId: "Confirm",
            mediatorType: null,
            prefabKey: new AssetKey("ui/common/confirm"),
            layer: UILayer.Popup,
            cacheable: false,
            singleton: false);
    }
}
