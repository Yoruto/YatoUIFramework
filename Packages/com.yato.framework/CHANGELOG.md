# Changelog

所有对本包的改动都记录在此文件。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [0.1.0] - 2026-10-29

### Added

- 建立 UPM 包骨架：`Runtime/{Core,Resources,Resources/Abstractions,UI}`、`Editor`、`Tests/{EditMode,PlayMode}` 六套程序集定义。
- 新增 `package.json`、`link.xml`（IL2CPP 裁剪兜底）、包级 `README.md`。
- 确立程序集依赖矩阵：Core → Abstractions → Resources，UI 只依赖 Core + Abstractions，Editor 仅编辑器平台。
- 新增 `Yato.Core.RuntimeHost`：`DontDestroyOnLoad` 宿主 + 每帧驱动 + 协程托管，业务不再手写 `Update`（T-P0-08）。
- 新增 `Yato.Core.MainThreadDispatcher`：跨线程投递队列，由 `RuntimeHost` 每帧消费。
- 新增 `Samples~/YatoFrameworkDemo`：示例脚本、`Example` 场景、`Example.prefab`、`Player.json` 迁入。
- 新增 `Yato.Mvc` 程序集：PureMVC 标准实现源码化（Interfaces / Core / Patterns），删除 `Assets/Plugins/PureMVC.dll`，可断点调试与裁剪。
- `Facade.ResetAll()` 在 `SubsystemRegistration` 阶段清空静态状态，关闭域重载重复进入 Play 不再报 `Facade Singleton already constructed`。
- 新增 Core 契约：`ILogger` / `UnityLogger` / `Log`（Debug 级仅 `UNITY_EDITOR || YATO_VERBOSE`）、`IEventBus` / `EventBus`（快照派发、异常必记录、`PublishOnMainThread`、Dispose 即退订）、`IServiceProvider` / `ServiceLocator`、`DisposableBag`、`AsyncOperationExtensions`。
- 新增资源契约：`IAwaitable` / `IAwaitable<T>`、`AwaitableExtensions.AsEnumerator`、`ResourceException`（含 `ResourceStage`）、`AwaitableCompletionSource` 默认实现。
- 新增资源抽象（UI 解耦前置）：`AssetKey`（自动规范化）、`IAssetHandle` / `IAssetHandle<T>`、`ResourceStats`、`IResourceService`、`IResourceProvider`、`NullResourceService`。
- 新增 UI 契约与实现：`UILayer`、`UIRoot`（DontDestroyOnLoad + 五层独立 Canvas）、`UILayerRoot`、`UIPageDef` / `UIPageContext` / `UIPageHandle`、`IUIPage` / `UIPageView` / `UIPageMediator<TView>` / `UIPageProxy`、`UIPageFactory`、`UIPageStack`、`UIPageCache`（LRU）、`UIRouter`（OpenAsync/Close/Back/IsOpen/CloseAll）、`UIMask`、`FrameworkNotifications`。
- 示例改写为新 API：`DemoPages` 页面表、`ExamplePageView`/`ExampleMediator`/`ExampleProxy`，`GameStart` 走 `UIRouter.Configure + OpenAsync`。

- 新增资源实现（P2 基础）：`ResourceService`（门面/引用计数/预加载/实例化）、`AssetHandle`（Interlocked 只释放一次）、`AssetEntry` / `BundleEntry`、`ResourceManifest`（schema v2 解析、asset→bundle 索引、依赖拓扑与环检测）、`RuntimePaths`（Windows/Android/iOS/WebGL 路径 + persistentDataPath 优先 + 远程地址）、零依赖 `MiniJson` 读取器。
- 新增三套 Provider：`EditorAssetProvider`（AssetDatabase，零打包开发）、`ResourcesProvider`（降级）、`BundleProvider` + `BundleLoader`（单飞加载、平台分流、失败抛 `ResourceException`）+ `BundleRefCounter`（引用计数、`HashSet` 去重的 5s 延迟队列、只 `Unload(false)`）。
- 新增 `ResourceServiceFactory`：编辑器默认 EditorProvider，`ForceBundleMode` 可强制读 AB，Player 下按 manifest 选择并在缺失时降级告警。
- 新增打包管线（`Yato.Editor/Build`）：`PlatformResolver`（平台目录唯一映射）、`BundleNaming`（目录即 bundle + AssetKey 推导）、`BundleBuilder`（LZ4/无 AppendHash/产物自检/同步 StreamingAssets）、`ManifestBuilder`（schema v2 + CRC32）、`BuildReportAnalyzer`（共享依赖提示）、`BuildPipelineEntry`（CI 与菜单同实现）。
- 目录规范落地：示例预制体迁至 `Assets/GameRes/UI/Example/Example.prefab`，页面 key 为 `ui/example/example`。
- 组件绑定现代化：新增 `ComponentBinder`（按名称查找 + `Validate` 重名/空引用/类型校验，修复 DEF-13）；旧 `ComponentAutoBindTool` 迁入 `Runtime/UI/Bind/` 并保留 GUID（预制体引用不断）；新增 `Editor/CodeGen/ComponentBinderValidator`（菜单 `Yato/UI/Validate Component Binders` 批量校验）。
- 删除 `Assets/ComponentAutoBindTool-master/`（嵌套工程、示例、旧 Inspector 生成器），其生成能力由 P4 的 `PageCodeGenerator` 承接。

- P3 异步体验：`UIRouter.CancelOpen` + 打开中请求表（取消后销毁实例，不回调、不产生孤儿）、`UILoading`（System 层运行时生成，超过 `LoadingThresholdSeconds` 才出现，支持进度）、弹窗 `UIMask` 联动（有弹窗显示、点击关闭最上层、全部关闭后隐藏）、`UIRouter.PreloadAsync` / `IsCached`、`UIListPool<T>`（Cell 复用，避免 Instantiate/Destroy 峰值）。

- P4 工具链：`CodeGenSettings`（生成目录默认 `Assets/YatoGenerated`）、`AssetKeyGenerator`（扫描预制体生成 `AssetKeys.g.cs`/`UIPages.g.cs`，内容无变化不写盘）、`PageCodeGenerator`（View/Mediator/Proxy/Bindings，已存在文件不覆盖）、`ResourceWindow`（常驻包/计数/耗时）、`LeakReportWindow`（退出 Play 汇总未释放句柄堆栈）、`ApiUsageValidator`（禁止 `Resources.Load` 直调）、`ResourceTracker`（句柄创建堆栈，仅编辑器）。

- P5 热更：`VersionManifest`（version.json 解析与 schema 校验）、`RemoteDownloader`（重试/进度/取消/CRC，文件级断点=已校验包跳过）、`HotUpdateService`（差量比对 → 下载 → CRC 校验 → 原子替换 manifest → 清理旧包 → 失败回退内置版本）、`Crc32Util`（运行时 CRC）、打包时同步产出 `version.json`、`HotUpdateWindow`（模拟远程：本地 Build 目录 + 静态服务器）。

- P6 测试与交付：EditMode（`AssetKeyTests` / `ManifestTests` / `BundleRefCounterTests` / `PageStackTests`）、PlayMode（`RouterLifecycleTests`：层级创建、无资源服务失败路径、取消打开、返回键）、`Documentation~/index.md`（QuickStart/API/热更/诊断/发布）、Samples README。
- Unity 6（6000.5.6f1）编译通过：修正 `UnityEngine.Resources` 命名空间歧义、`UnityEngine.AwaitableCompletionSource<T>` 同名歧义、`AssetBundleManifest` 的命名空间引用；示例补充独立 asmdef；移除不兼容的 `com.unity.collab-proxy` 与 `com.unity.timeline 1.7.6`。
- 绑定校验补全 TMP 字体检查：`font == null` 记为错误（运行时显示空白），`fontSharedMaterial` 缺失记警告；同时校验「绑定名与节点名一致」与预制体命名规范（ASCII / PascalCase）。`Yato.Editor` 增加 `Unity.TextMeshPro` 引用。
- 新增一键冒烟 `Yato/Debug/Run Smoke Test`：自动进入 Play，校验 AssetKey 规范化、manifest 解析与依赖顺序、循环依赖检测、bundle 引用计数去重卸载、EventBus 异常隔离、DisposableBag 逆序释放、ServiceLocator、UIRoot 五层 Canvas、UIMask/UILoading 显隐、UIListPool 复用，输出 PASS/FAIL 清单后退出 Play；batchmode 下以退出码反映结果。

### Removed（P2）

- 移除旧组件绑定工具的编辑器 Inspector 与规则辅助器（`IAutoBindRuleHelper` / `AutoBindGlobalSetting`）。

### Removed（P1）

- 删除旧框架：`UIManager`、`BaseMediator`、`BaseProxy`、`BaseUI`、`BaseUIView`、`Singleton<T>`、`UIDefine`、`UIFrameworkData`、`CreateUICommand`（DEF-07/09/11/12 一并消除）。
- 页面生命周期不再依赖场景里预摆的 `UI/Canvas` 与字符串反射（`Type.GetType` + `Assembly.CreateInstance` 全部移除）。

### Changed

- `UIManager` 改为继承 PureMVC `Facade`，单例改用 `Facade.GetInstance(() => new UIManager())`（DEF-02）。
- Proxy 构造统一传 `{PageId}Proxy`，不再出现 `null`/空 key（DEF-03）。
- 页面初始化顺序改为「注册 Proxy → 注册 Mediator → 激活 GameObject」，首帧即可取到数据（DEF-04）。
- 初始化队列 `try/finally` 强制清理，单页异常不再每帧重试刷屏（DEF-06）。
- 重复打开同一 PageId 返回已有实例，不再产生孤儿对象（DEF-05）。
- 反射建类改为 `ResolveType` + `Activator`，类型缺失时给出明确报错而非 NRE（DEF-08 局部）。

### Removed

- 移除框架代码对 `UnityEditor.PackageManager`、`Unity.VisualScripting` 的引用，项目 `manifest.json` 同步移除 `com.unity.visualscripting`（DEF-01）。
- 删除 `SingletonFacade<T>` 与 `SingletonMonoBehaviour<T>`（由 `Facade.GetInstance` / `RuntimeHost` 取代）。
- 删除 `CreateUICommand`（由 `UIRouter.OpenAsync` 取代）、删除 `LitJson`（示例改用 `JsonUtility`）。
- 框架不再注册任何业务命令（DEF-10）。
