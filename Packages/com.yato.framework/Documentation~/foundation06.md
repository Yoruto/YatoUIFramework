# 0.6 接入、诊断与生命周期

仅支持 Unity 6，当前开发与验收为 6000.5.6f1。沿用同步 Bootstrap、协作取消、显式存档恢复及资源范围所有权。

## 异步互操作

在 Play Mode 的主线程、RuntimeHost 已启动后创建桥接。Yato.Resources.TaskBridge 提供 AsTask / AsAwaitable 及 IAwaitable 的 GetAwaiter，可直接 await 资源与流程操作。转换会分配 Task/闭包，不用于每帧或实例池取还热路径。

~~~csharp
using Yato.Resources;
using Yato.Core;

// 取消传给原操作；桥接不提前结束共享加载、不额外获取资源引用。
var handle = await resources.LoadAsync<UnityEngine.GameObject>(key, RuntimeHost.LifetimeToken);
try { /* use handle.Asset */ }
finally { handle.Dispose(); }

// 从主线程调用，Task 的异常与取消保留，完成通知回到主线程。
IAwaitable saving = saves.SaveAsync("slot1", data, RuntimeHost.LifetimeToken).AsAwaitable();
await saving;
~~~

AsTask 忠实等待原 IAwaitable 完成，即使宿主已退出；需要随宿主取消时，将 LifetimeToken 传给原操作。AsAwaitable 在宿主退出后取消适配器，原 Task 继续运行且其迟到异常仍被观察。转换不自动 Dispose 任何结果；原 Task 的资源型返回值仍由调用方负责，不应把带租约的 Task 当作可抛弃操作。FullSample 示范 await + ResourceScope，并在资源接管前失败时释放局部租约。

await Task 会恢复调用方捕获的同步上下文；从后台调用的业务代码仍须明确回主线程再操作 Unity 对象。不要在 Unity 主线程调用 Wait()/Result 阻塞等待。

完成源固定首次结果；一个 Completed 观察者抛异常不再中断后续观察者。

## 框架诊断

菜单 Yato / Tools / Framework Diagnostics，显示资源句柄以及 Flow、ResourceScope、UI、Pooling、Pause、Save 快照。Save/UI.Input 保持可选；Editor 不引用它们的程序集。

ResourceService.Stats.ActiveAssetCount 按仍被句柄持有的不同 key 计数，不等于句柄数量或 Unity 物理内存中的资产数量；ReleaseUnused 不会将仍持有的资产错误归零。Stats/Capture 属于允许分配的诊断接口，不用于每帧热路径。

自定义模块实现 Core 的 IDiagnosticSource，调用 DiagnosticRegistry.Register(this)。注册使用弱引用，返回 IDisposable 可显式退订；销毁后不应再使用对象。快照值为分离的只读副本，捕获失败隔离，关闭 Domain Reload 时清理注册。Capture 应在主线程显式调用；快照构造允许分配，不会自动每帧采样。存档诊断只显示操作、槽位、版本和错误，不输出 DTO 载荷。

## 接入向导与项目检查

菜单 Yato / Setup / Create Starter：选择尚不存在的 Assets 子目录，生成 AppBootstrapConfig、Pages 目录资产、中性页面预制体、BootstrapRoot 预制体和 Bootstrap 场景。可选将场景追加到构建列表。不会覆盖已存在目录，不改变当前打开场景和未保存状态。

生成 UIStarter 使用注入的 IResourceServiceSource，默认由 ResourcesServiceHost 提供纯 Resources 服务；UI 仍只依赖资源抽象。自定义应用可继续自行配置 UIRouter。

菜单 Yato / Tools / Project Doctor：集中检查 ConfigCatalog、UIPageCatalog、组件绑定、默认选中、构建场景、Bootstrap、EventSystem 和输入模式；错误可定位资产。Editor 构建前执行同一检查。其他可选模块通过 Core.IProjectValidationSource 提交资产校验，不要求 Editor 引用可选包。

UIPageCatalog 为简单无 Mediator 页面提供 ScriptableObject 注册。RequireResourcesPrefabs=true 时检查 Resources 预制体缺失及同 key 冲突；Bundle 项目应关闭该选项并使用自己的清单校验。复杂 MVC 页面可继续使用原有 UIPageDef 和代码生成，不强制迁移。

可选 UI.Input 新增 UIInputProfile：引用源 InputActionAsset、基础/UI map 和动作路径。校验缺 map/动作、动作所属 map 及控制类型。使用 new UIInputCoordinator(input, eventSystem, profile) 时对运行时副本复用校验，既有字符串构造函数保留。

## 资源与页面生命周期

内置 `ResourceService` 实现 `IDisposable`。拥有服务的应用应先结束使用方并关闭页面，再释放服务；关闭会取消自己的等待、释放已发出的句柄并回收迟到加载。`ResourceServiceFactory.Create()` 自己创建的 Provider 由服务拥有；注入 Provider 默认仍由调用方拥有，可传 `ownsProvider: true` 显式转移所有权。共享 Provider 不应被任意一个使用方关闭。

`PreloadAsync` 在首个加载失败时传播异常并结束，不再将部分成功报告为完整成功；已经加载的临时句柄仍会释放。取消同样传播给调用方。

`UIRouter.Configure` 会取消在途打开，旧结果不得激活页面；已经打开的页面保留原生命周期。路由页面按注册 → `OnCreate` → 入栈 → `OnOpen` 执行，独立调用 `UIPageFactory.CreateAsync` 仍在返回前执行 `OnCreate`。同一个实例可能重复收到 `OnOpen`，事件绑定必须先清理旧绑定或保持幂等。

Settings 同一实例的加载读取按顺序执行；更新的 Load/Apply/Reset 使旧加载返回 `SettingsLoadStatus.Superseded`，不覆盖状态或 `LastLoadResult`。文件忙时抛 `FileBusyException` 并保留当前值；同步 Load 遇到在途异步加载也会立即报告文件忙，不阻塞主线程。`UIListPool.Get(index)` 会将活动范围扩展到 `0..index`，调用 `SetCount` 可缩小该范围。

## 验证

手动消费者准备工具增加 UnityLifecycleVerification：在独立工程运行 8 次 Play，覆盖 Domain Reload / Scene Reload 的四种组合并恢复测试前设置。测试不应在用户正在编辑的主工程中执行。源工程另含 100 轮真实场景卸载、Flow/UI/Pooling/Pause 的基线回归。

IL2CPP 依赖当前 Editor 的支持模块、C++ 工具链和 Windows SDK；Mono 通过不能替代 IL2CPP 通过。失败、跳过及环境待验收独立记录。没有新增 CI 或 Yato Survivor 内容。

实际结果及环境限制见 [0.6 验收记录](foundation06-verification.md)。
