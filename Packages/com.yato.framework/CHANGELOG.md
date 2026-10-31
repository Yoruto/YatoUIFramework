# Changelog

所有对本包的改动都记录在此文件。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [0.5.0] - 2026-11-01

- 新增资源实例池和值类型代次句柄，支持预热、容量和重复归还保护。
- App 新增暂停令牌、生命周期信号和等待异步保存的显式退出协调器。
- UI 新增无 Facade 事件、焦点策略、可选非缩放时间过渡和 CloseAsync；同步 Close 保持原行为。
- 新增可选 UI/Input 适配，接管及恢复事件模块、输入上下文和嵌套焦点。
- Flow/Config/Pooling 加入聚合包；Save/Input/UI.Input 仍可选。Yato Survivor 整体延期。

## [0.4.0] - 2026-11-01

- 新增独立 Flow/Config 模块和可选 Save 包，保持同步 Bootstrap 与基础 Persistence 契约。
- 配置目录复制运行数据，统一校验 ID/数值/引用并接入 Player/Bundle 构建前检查。
- 存档支持槽位、备份、显式恢复与内存迁移，损坏时禁止静默覆盖；附计数器示例。

## [0.3.1] - 2026-11-01

- 检查 BundleBuildResult，批处理失败返回非零；自检失败不复制产物。
- 显式启动缓存维护：保留当前/上一版本，资源已使用时拒绝清理，返回数量/字节/原因。

## [0.3.0] - 2026-10-31

### Added

- 热更新统一 BundleRelativePath 校验、清单字段范围/数量/平台/重复与路径碰撞检查、链接路径拒绝、受限流式下载、超时与生产 HTTPS 默认策略。
- 版本暂存、完整资源/依赖校验、D04 FileStore 原子活动指针提交、跨实例 Busy 和取消结果；失败保留最后成功版本，旧平铺缓存只读兼容。
- BasicSample / FullSample 自带场景与预制体，无原工程 Assets/GameRes 或 TMP 字体依赖；不使用全局自动示例注册。
- 可选 Input 包 0.1.0：游戏提供 action asset，运行时副本、上下文、对称订阅、交互重绑定与 Binding Override JSON。
- 独立消费工程准备/验证工具、故障回归用例、安装/升级说明与 UI/MVC 可行性结论。无 CI 配置。

### Changed

- **支持范围变化**：仅支持 Unity 6，全部 Yato 包的 `unity` 字段统一为 `6000.0`，移除旧 `unityRelease: 13f1`；取消旧 Unity 大版本的兼容验收要求，当前开发与实测版本为 `6000.5.6f1`。
- Resources 升为 0.2.0，依赖 Persistence 0.1.0 以复用安全提交；其余包的对应依赖同步。
- **兼容性变化**：Bundle 模式默认 BundleRequired，初始化失败抛包含 Provider/平台/路径/阶段/原始异常的错误；显式 AllowResourcesFallback 才降级。编辑器默认 AssetDatabase 不变。
- **兼容性变化**：远程默认只接受 HTTPS，file:// 与 loopback HTTP 需要显式测试开关；不接受跨平台、非规范 Bundle 名或不合法的 CRC/大小值。
- Provider 固定创建时的快照，缺包不跨目录混用；已提交版本保守保留，当前不自动清理，避免破坏旧句柄。升级见安装指南。
- Package Manager 推荐样例改为 BasicSample / FullSample；旧 YatoFrameworkDemo 源码保留供迁移，AppBootstrapSample 改为显式注册。

### Fixed

- 下载先删除目标导致最后可用文件丢失；更新失败删除活动 manifest；清单提交前覆盖活动 Bundle。
- AppBootstrapSample 保存并对称移除场景委托，重复 Initialize/Shutdown 与初始化失败清理不叠加订阅。
- CRC 从整文件读取改为 64 KiB 缓冲流式计算。

## [Unreleased]

### Fixed

- `ResourceService`：最后一个消费者取消后，将旧 Provider 加载标记为不可复用；新请求重新启动加载，旧任务即使迟到成功也独立归还引用。
- `SceneService`：关闭期间不提前释放互斥通道；正在执行的场景加载与初始化器安全结束后才作废旧结果，避免过期协程恢复上下文。
- 场景卸载失败时保留既有资源句柄，卸载中拒绝迟到资源接管，失败后恢复资源登记；卸载成功后再释放。
- `GameBootstrap`：关闭后清理 `Scene` 服务定位注册，重新初始化时恢复注册并复用原场景服务，保留未结束场景操作的互斥状态。
- 新增资源取消后重试、场景关闭与卸载失败、Bootstrap 重启的回归用例。

## [0.2.0] - 2026-10-31

### Added

- 新增 `com.yato.app`：`GameBootstrap`（DDOL 根、幂等初始化、按 `Order` 初始化/反序关闭、失败回滚、Domain Reload 静态重置、Editor-only 自动补初始化）与 `SceneService`（Single/Additive、互斥通道忙时立即 Busy、`Success/Busy/Canceled/InvalidScene/Failed` 五态与失败阶段、`SceneContext` 资源范围、`Loaded/Ready` 分离、可选 `ISceneInitializer` 与 60s 可配置超时、超时后等待旧任务结束才释放通道）。
- 新增 `com.yato.persistence`：`PersistencePaths`（persistentDataPath 单根、拒绝绝对路径/`..`/盘符与数据流、逐段符号链接逃逸检查）、`IFileStore`/`FileStore`（同步 + 异步、同路径互斥抛 `FileBusyException`、同目录唯一临时文件 + `File.Replace` 原子提交、提交失败抛 `FileCommitException` 且不降级覆盖、提交前可取消、残留临时文件不参与枚举且可安全清理）、`ISerializer` / `JsonUtilitySerializer`（UTF-8 无 BOM）。
- 新增 `com.yato.settings`：`SettingsService<T>`（默认值模板隔离、默认值 → 用户文件 → 自定义校验 → 发布、缺文件用默认值、损坏/非法保留原文件并回退、`Changed` 事件逐订阅者隔离、显式 `Load/Save` + 异步、`ResetToDefaults`）。
- 新增 `com.yato.audio`：`AudioService`（Master/Music/Sfx/Ui/Voice 通道、双 Music 交叉渐变、SFX 池与 `DropNewest`/`ReplaceLowestPriority` 策略、2D/3D、AudioClip 句柄生命周期、迟到加载结果释放、可选 `AudioMixerVolumeSink`）。
- 新增 `com.yato.input` 占位包（D07 待审核，仅 `package.json` + README，暂不被聚合包依赖）。
- 新增测试：`ResourceServiceTests`、`UIPageCacheTests`、`RouterConcurrencyTests`、`ResourceScopeTests`、`SceneServiceTests`、`GameBootstrapTests`、`FileStoreTests`、`SettingsServiceTests`、`AudioServiceTests`。
- 新增 `docs/YatoFramework_ManualVerification_2026-10-31.md`：分阶段手动验证清单与 §5.2 最小集成验收流程对照。
- 示例新增 `Scripts/AppBootstrapSample.cs`：`BeforeSceneLoad` 注册 `IAppService`，演示设置→音频音量联动与场景事件订阅。

### Changed

- **结构拆分**：原单包拆为 `com.yato.core` / `mvc` / `resources` / `ui` / `app` / `persistence` / `settings` / `audio` / `editor`，`com.yato.framework` 改为聚合元包（仅依赖子包 + `Tests` + `Samples~` + `Documentation~`）。
- 程序集名称保持不变（`Yato.Core` 等），asmdef 引用、示例与测试无需改动；`.meta`/GUID 随目录迁移保留。
- `link.xml` 从包级单一文件拆到各运行时包，按包保留自身程序集。
- `Packages/manifest.json` 增加 `"testables": ["com.yato.framework"]`，包内测试程序集才会被 Unity 编译。
- 示例 asmdef 增加 `Yato.App` / `Yato.Persistence` / `Yato.Settings` / `Yato.Audio` 引用。

### Fixed

- 资源引用计数增删不对称：同一 key 的并发/重复加载改为单飞，句柄归零只归还一次 Provider 引用；取消/类型不符时无人接管的引用立即回滚。
- `BundleProvider` 依赖链失败、取消、资产缺失时逐层回滚已 AddRef 的 bundle。
- `IAssetHandle.RefCount` 改为实时读取，不再返回创建时快照。
- UI：缓存恢复后同时存在于栈与缓存、`UIPageCache.Add` 同 key 静默替换导致实例泄漏、同页并发打开产生多个实例、`Activate` 失败不回收实例、过期句柄误销毁在用实例、按层 `CloseAll` 不清理该层缓存。
- Domain Reload 关闭时 `RuntimeHost` / `MainThreadDispatcher` / `ResourceServiceFactory` / `UIRouter` 的静态状态与订阅残留；`UIRouter` 重置时释放栈与缓存中的句柄。

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
