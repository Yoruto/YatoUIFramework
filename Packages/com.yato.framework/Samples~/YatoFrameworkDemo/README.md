# YatoFrameworkDemo

示例工程，不会被自动编译。通过 Package Manager（`Yato Framework` → Samples）导入到 `Assets/Samples/...` 后使用。

## 内容

| 文件 | 说明 |
|---|---|
| `Scripts/GameStart.cs` | 入口：创建门面 → `UIRouter.Configure(ResourceServiceFactory.Create())` → `OpenAsync` |
| `Scripts/DemoPages.cs` | 页面表（P4 的 `AssetKeyGenerator` 会生成等价的 `UIPages.g.cs`） |
| `Scripts/ExamplePageView.cs` | `UIPageView` 子类，打印 OnOpen/OnCover/OnReveal/OnClose |
| `Scripts/ExampleMediator.cs` | `UIPageMediator<ExamplePageView>`，演示 `CreateProxy` 与通知订阅 |
| `Scripts/ExampleProxy.cs` | `UIPageProxy`，演示 `BindModel` / `RefreshView` |
| `Scripts/ExampleJsonLoad.cs` | 用 `JsonUtility` 读 `Resources/player.json`（框架不再绑定 JSON 库） |
| `Resources/Player.json` | 上例的数据 |

## 使用

1. 预制体 `Example.prefab` 已放在主工程的 `Assets/GameRes/UI/Example/`（目录规范），示例页面 key 为 `ui/example/example`；
2. 把 `GameStart` 挂到场景任意物体上，Play 即可；
3. 编辑器下走 `EditorAssetProvider`（不读 AB）；想验证 AB，把 `ResourceServiceFactory.ForceBundleMode` 设为 true 并先打包。

## 注意

- 示例自带 `Yato.FrameworkDemo.asmdef`，依赖 `Yato.Core` / `Yato.Mvc` / `Yato.Resources.Abstractions` / `Yato.Resources` / `Yato.UI` 与 `Unity.TextMeshPro`；
- 若目标工程不需要 TMP，把 `ExamplePageView` 里的 `TextMeshProUGUI` 换成 `UnityEngine.UI.Text` 并从 asmdef 移除该引用；
- 若报 `未注入 IResourceService`，说明 `UIRouter.Configure` 未被调用或资源服务创建失败（看 Console 的 `[Yato.Resources]` 日志）。
