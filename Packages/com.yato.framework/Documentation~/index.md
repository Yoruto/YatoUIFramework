# Yato Framework 快速上手

当前整体版本为 **0.7.0**。按需安装、0.6 → 0.7 工厂迁移与快照布局见[安装与升级](installation.md)，现场与独立工程结果见[当前验证记录](verification.md)。0.6 的异步/诊断 API 见 [0.6 指南](foundation06.md)，基础模块用法见[基础模块指南](foundation.md)。

向其他项目交付框架时，可使用模块 TGZ 或带安装器的离线 ZIP；包集选择、导出命令与 manifest 限制见[UPM 模块分发](../../../docs/UPM_DISTRIBUTION.md)。模块版本独立维护，整体版本 `0.7.0` 不代表每个模块的版本号。

新项目可导入 BasicSample / 离线 FullSample；需要资源热更新时，再从 `com.yato.resources.hotupdate` 导入 HotUpdateSample。完整聚合包会带入 Bundle/HotUpdate，精简项目直接安装所需子包。

## 1. 30 分钟接入

自 `0.2.0` 起框架由多个 UPM 包组成，`com.yato.framework` 是聚合元包：

1. 按[安装表](installation.md)把所需 `com.yato.*` 完整目录放入工程 `Packages/`，保留 `.meta` 和 `link.xml`（内嵌包无需在 manifest 重复登记）；
   仅资源需 Core + Resources，最小 UI 再加 Mvc + UI。需要代码生成或向导时，另装 `com.yato.editor` 及其依赖；无需安装 Bundle/HotUpdate；
2. 预制体放到 `Assets/GameRes/UI/{Module}/{Page}.prefab`，资源 key 规则：`ui/{module}/{page}`（全小写、无扩展名）；
3. 选中预制体 → 菜单 `Yato/CodeGen/Generate Page Code (Selection)` 生成 `View/Mediator/Proxy/Bindings`；
4. 菜单 `Yato/CodeGen/Generate Asset Keys && Pages` 生成 `Assets/YatoGenerated/{AssetKeys,UIPages}.g.cs`；
5. 注册全局服务并初始化：

```csharp
public static class GameEntry
{
    // 必须早于 Bootstrap：编辑器自动初始化 / Bootstrap Scene 都在场景加载后初始化
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        GameBootstrap.RegisterService(new MySaveService());
        GameBootstrap.RegisterService(new MySettingsService());
    }
}
```

`GameBootstrap.Initialize()` 由 Bootstrap Scene 里的组件或编辑器自动补初始化触发；重复调用幂等，失败会回滚已完成的服务并抛 `AppInitializationException`。

## 2. 页面生命周期

```
OpenAsync: 加载 → 实例化(隐藏) → 创建 Mediator/Proxy → 注册 → OnCreate
           → 入栈(旧页 OnCover) → OnOpen(args) → SetActive(true)
Close:     OnClose(destroy) → 出栈(新栈顶 OnReveal) → 注销 → 销毁 或 进 LRU
```

- `UIPageView`（MonoBehaviour）与 `UIPageMediator<TView>` 二选一或组合使用；
- Mediator 必须提供 `(string mediatorName)` 构造函数（多实例命名）；
- 关闭后若 `Cacheable && Singleton` 则进入 LRU（默认容量 8，`UIRouter.CacheCapacity`）；
- 同一页面加载中重复 `OpenAsync` 会共享同一个结果（单飞），不会产生两个实例；
- 已打开单例再次 `OpenAsync` 会调用 `OnOpen`，事件订阅与控件绑定应保持幂等；
- `UIRouter.CancelOpen` 立即以取消结束调用方等待；底层无法中止的 Unity 请求可继续收尾，迟到实例/句柄会被回收；`CloseAll` 也取消目标范围的在途打开请求；
- Reset/Configure 会使旧打开请求失效，不能让旧请求激活到新的路由会话。

## 3. 资源与打包

| 场景 | 安装与初始化 |
|---|---|
| 基础资源 | Core + Resources；`ResourceServiceFactory.Create()` 在 Editor 用 `EditorAssetProvider`，Player 用 `ResourcesProvider` |
| 显式 Resources-only | `ResourceServiceFactory.Create(new ResourcesProvider(), ownsProvider: true)`，Editor/Player 行为一致 |
| 本地 AssetBundle | 加装 Resources.Bundle；`BundleResourceServiceFactory.Create/CreateAsync()` 使用 `BundleProvider`，无需热更包 |
| Bundle manifest 失败 | 默认 `BundleRequired` 抛错；显式 `ResourceFailurePolicy.AllowResourcesFallback` 才改用 `ResourcesProvider` |

从 0.6 升级时，Bundle 初始化从原资源工厂迁到 `BundleResourceServiceFactory`；`ForceBundleMode` 已移除。使用 Bundle 或热更类型的程序集分别引用 `Yato.Resources.Bundle` / `Yato.Resources.HotUpdate`，公开类型仍位于 `Yato.Resources` 命名空间。基础资源服务不负责自动发现或启用可选后端。

Android/WebGL 的内置 `StreamingAssets` manifest 是 URL，必须先等待异步工厂，再启动依赖资源的 UI/服务：

```csharp
var opening = BundleResourceServiceFactory.CreateAsync();
opening.Completed += () =>
{
    IResourceService resources = opening.GetResult();
    UIRouter.Configure(resources);
    // 在安全切换点或退出时 Dispose 此服务。
};
```

同步 `Create()` 适用于本地文件系统 manifest；不会替 Android/WebGL 的同步 `IAppService.Initialize()` 自动等待 URL 请求。需要在预加载入口完成资源初始化后再注入业务服务。该跨平台路径已有代码与取消回归，移动端/WebGL Player 尚未实机验收。

安装 Bundle 包后，打包工具随其 Editor 程序集提供：菜单 `Yato/Build/Bundles (Active Platform)` 或本机手动命令：

```
Unity.exe -batchmode -quit -projectPath <path> \
  -executeMethod Yato.Editor.BuildPipelineEntry.BuildBundles -buildTarget Android
```

产物：`Build/AssetBundles/{Platform}/manifest.json + version.json + bundles`，并同步到 `Assets/StreamingAssets/AssetBundles/{Platform}/`。

资源句柄规则：同一 key 多次加载共享一次 Provider 引用，全部 `Dispose` 后才归还；取消或类型不符时不会留下悬挂引用。内置服务支持 `IDisposable`：默认创建的 Provider 随服务关闭，注入 Provider 仅在显式转移所有权时级联释放。

## 4. 场景与启动（D02 / D03）

```csharp
// 默认 Single；Additive 需显式指定
var request = new SceneLoadRequest("Gameplay", LoadSceneMode.Single)
    .WithInitializer(new GameplayInitializer())   // 可选：完成后才 Ready
    .WithReadyTimeout(30f);

IAwaitable<SceneLoadResult> run = GameBootstrap.Scene.LoadAsync(request);
run.Completed += () =>
{
    SceneLoadResult result = run.GetResult();
    // Success / Busy / Canceled / InvalidScene / Failed + Stage
};
```

- 场景操作共用一个互斥通道，忙时立即返回 `Busy`，不排队；
- `Loaded` 只表示 Unity 场景已加载，不保证 `Start` 已执行；`Ready` 表示初始化器完成；
- 初始化器失败不自动回滚场景；超时后先请求取消，但**旧任务真正结束前不释放通道**；
- 每个 `SceneContext` 有独立 `Resources` 范围：`context.Resources.Track(handle)` 的句柄在卸载时逆序释放；范围结束后迟到的加载结果立即回收；
- 不允许卸载最后一个可用场景。

## 5. 设置与音频（D05 / D06）

```csharp
var settings = new SettingsService<GameSettings>(
    new GameSettings(),                 // 默认值（模板，运行期不会被改写）
    new FileStore(),                    // 根目录默认 persistentDataPath
    "settings/game.json");              // 相对路径，游戏决定布局

settings.Load();                        // 默认值 → 用户文件 → 校验 → 发布
settings.Apply(s => s.MusicVolume = 0.5f);
settings.Save();                        // 显式保存；Apply 不会自动落盘

var audio = new AudioService(ResourceServiceFactory.Create(),
    new AudioServiceOptions { SfxPoolCapacity = 16 });
audio.SetBusVolume(AudioBus.Music, settings.Current.MusicVolume);

// 播放（音乐交叉渐变；SFX 返回句柄，可停止/绑定场景范围）
audio.PlayMusic(musicClip, new MusicOptions { FadeSeconds = 0.6f });
ISfxHandle sfx;
audio.TryPlaySfx(new SfxRequest(new AssetKey("audio/ui/click")) { Bus = AudioBus.Ui }, out sfx);
```

- 设置文件缺失用默认值；损坏或校验不通过时保留原文件、回退默认值并给出 `SettingsLoadResult`；
- Audio 不读写设置，音量由游戏层订阅设置变化后调用 `SetBusVolume`；
- 循环音效可用 `context.Resources.Track(sfxHandle)` 绑定到场景，卸载时自动停止。

## 6. 热更

先安装 `com.yato.resources.hotupdate` 及 Bundle/Persistence 依赖；基础资源和本地 Bundle 项目无需安装。

1. 打包后把 `Build/AssetBundles` 作为远端根目录（本地可用 `python -m http.server 8080`，需显式开启 loopback HTTP 测试选项）；
2. 运行时：

```csharp
var service = new HotUpdateService();
var run = service.RunAsync(RuntimePaths.RemoteRootUrl);
run.Completed += () => {
    var result = run.GetResult();
    // 成功后在业务安全切换点关闭旧 UI/资源服务，
    // 通过 BundleResourceServiceFactory.Create/CreateAsync 创建读取新快照的服务。
};
```

失败/提交前取消仅清理本次暂存，保持上一成功远程快照；首次失败才继续使用 StreamingAssets。新 Provider 固定活动快照，旧 Provider 保持旧快照。Play/Player 默认安装热更缓存策略；EditMode 工具可显式调用 `HotUpdateBundleCachePolicy.Install()`。Bundle 包本身只依赖可替换的缓存策略接口。

生产默认 HTTPS；本地测试通过 `new RemoteDownloader { AllowFileUrls = true, AllowInsecureLocalhost = true }` 显式放行相应测试地址。首次创建 Bundle Provider 前可进行启动缓存维护，保留当前/上一快照；已使用或不安全的根目录会拒绝清理，运行中不清理已提交快照。详见 [安装指南](installation.md)。

## 7. 诊断

| 菜单 | 作用 |
|---|---|
| `Yato/Tools/Resource Window` | 常驻 bundle / 引用计数 / 加载耗时 |
| `Yato/Tools/Leak Report` | 退出 Play 汇总未释放句柄堆栈 |
| `Yato/Tools/Check Forbidden APIs` | 检查业务代码是否直接调用 `Resources.Load` |
| `Yato/UI/Validate Component Binders` | 预制体绑定校验（重名/空引用） |
| `Yato/HotUpdate/Settings` | HotUpdate 包提供的设置 / 模拟远程地址 |
| `Yato/Debug/Run Smoke Test` | 基础冒烟：自动进 Play 跑 7 项校验，输出 PASS/FAIL 后退出 |
| `Yato/Debug/Run Bundle Smoke Test` | Bundle 包提供的 manifest、依赖环和引用计数 3 项校验 |

## 8. 测试

- EditMode：`AssetKey` / manifest / bundle 引用计数 / 页面栈 / 页面缓存 / 资源引用计数 / 资源范围 / 文件存储 / 设置；
- PlayMode：UI 生命周期与并发、App/Scene 集成、音频池与渐变；
- 运行：Unity Test Runner（EditMode / PlayMode），或本机手动用 `-runTests -testPlatform EditMode`；
- 包内测试要求宿主工程 `Packages/manifest.json` 含：

```json
"testables": ["com.yato.framework", "com.yato.input"]
```

未安装 Input 包时，省略其中的 `com.yato.input`。当前现场结果为 EditMode **195 / 195**、PlayMode **120 / 120**，另有基础 **7 项**与 Bundle **3 项**冒烟；独立安装结果见[模块拆分摘要](../../../docs/verification/MODULAR_RESOURCES_2026-11-07.json)，现场会话见 [Live Unity 摘要](../../../docs/verification/LIVE_UNITY_2026-11-07.json)，验收边界见[验证记录](verification.md)。

## 9. 发布流程

1. 更新各包 `package.json` 版本与 `CHANGELOG.md`；
2. 按[验证记录](verification.md)核对本次发布目标；Windows 结果不能替代移动端/WebGL 或 IL2CPP 验收；
3. 为目标平台构建并校验 bundles/manifest，保留测试结果与产物哈希；
4. 外部工程导入包并跑通示例与手动验证清单（`docs/YatoFramework_ManualVerification_2026-10-31.md`）。
