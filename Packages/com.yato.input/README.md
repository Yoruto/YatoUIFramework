# Yato Input 0.1.0

可选的 Unity Input System 薄适配，程序集 `Yato.Input`。复制此包到 Packages 后，UPM 安装 `com.unity.inputsystem` 1.7.0 起。聚合包及其他运行时包不依赖 Input；不需要时移除此包即可。

`InputContextService` 克隆游戏提供的 InputActionAsset，拥有其运行期副本和订阅。游戏原始 asset 不会被启用、禁用或写入 override；游戏只管理此服务公开的 Actions，不要将 PlayerInput 对原 asset 的控制与这个副本混用。所有调用在 Unity 主线程执行。

```csharp
using Yato.Input;
using UnityEngine.InputSystem;

var input = new InputContextService(myActions);
input.EnableContext("Gameplay"); // 名字由游戏定义，默认排他切换
var subscription = input.Subscribe("Gameplay/Jump", context => Jump());
input.EnableContext("UI");
input.DisableAll();

// 恢复被暂时禁用 action 的状态，ESC 取消；上下文切换会先取消。
input.EnableContext("Gameplay");
input.StartRebinding("Gameplay/Jump", 0, success => RefreshBindingLabel());
input.CancelRebinding();

string json = input.SaveBindingOverrides();  // 游戏决定何时通过 Persistence/Settings 保存
input.LoadBindingOverrides(json);
input.ResetBindingOverrides();

subscription.Dispose(); // 可放入 DisposableBag；服务 Dispose 也会统一退订
input.Dispose();        // 取消重绑定、移除订阅、禁用并销毁运行时 asset；重复调用安全
```

ActionMap 名称与 action 路径、绑定索引由游戏提供。Composite 根不能一次重绑定，请逐个重绑定其子 binding。首版同一服务只允许一个重绑定操作；没有本地多人协调、设备图标或输入录制。不要在重绑定完成/取消回调中递归启动另一个重绑定或切换上下文。

Project Settings → Player → Active Input Handling 按游戏选择 Input System 或 Both。基础/完整示例使用 OnGUI，不要求旧 Input Manager；不要在仅启用新 Input System 时调用旧 UnityEngine.Input API。

测试：在 Packages/manifest.json 的 testables 加 `com.yato.input`，手动运行 EditMode 的 `Yato.Input.Tests` 和 PlayMode 的 `Yato.Input.Tests.PlayMode`。未安装本包的消费工程不会编译这些测试。

实现复用 [Unity 官方交互式重绑定与 Binding Override JSON](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.7/manual/ActionBindings.html#interactive-rebinding)，存储策略留给游戏。
