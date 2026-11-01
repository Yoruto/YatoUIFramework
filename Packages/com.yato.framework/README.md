# Yato Framework

Unity 通用框架（UPM 多包）。`com.yato.framework` 自 `0.2.0` 起是**聚合元包**：自身不含运行时代码，只依赖各子包并附带示例、测试与文档。

当前聚合版本 **0.6.0**。Flow / Config / Pooling 为默认依赖；Save / Input / UI.Input 独立可选。新增异步桥接、统一诊断和项目接入工具见 [0.6 指南](Documentation~/foundation06.md)。既有模块见 [基础指南](Documentation~/foundation.md)。

## 包结构

| 包 | 程序集 | 依赖 | 说明 |
|---|---|---|---|
| `com.yato.core` | `Yato.Core` | UnityEngine | RuntimeHost、主线程投递、事件总线、服务定位、日志、异步工具 |
| `com.yato.mvc` | `Yato.Mvc` | — | PureMVC（Facade / Mediator / Proxy / Command） |
| `com.yato.resources` | `Yato.Resources`、`Yato.Resources.Abstractions` | Core、Persistence + assetbundle / jsonserialize / unitywebrequest 模块 | 资源句柄与引用计数、Provider（Editor / Resources / Bundle）、热更、诊断 |
| `com.yato.ui` | `Yato.UI` | Core、Mvc、Resources、`com.unity.ugui` | 页面路由、层级与页面栈、LRU 缓存、加载遮罩、绑定 |
| `com.yato.app` | `Yato.App` | Core、Resources（Abstractions） | `GameBootstrap` 幂等初始化、`SceneService`（Single/Additive、五态、Ready 初始化器） |
| `com.yato.persistence` | `Yato.Persistence` | Core | 安全相对路径、UTF-8 JsonUtility 序列化、临时文件安全替换、同路径互斥 |
| `com.yato.settings` | `Yato.Settings` | Core、Persistence | 强类型设置：默认值 → 用户文件 → 校验 → 发布、变化事件、显式 Load/Save |
| `com.yato.audio` | `Yato.Audio` | Core、Resources | 通道音量、双 Music 交叉渐变、SFX 池与溢出策略、AudioClip 句柄 |
| `com.yato.editor` | `Yato.Editor` | 运行时包 + `com.unity.textmeshpro` | 打包、代码生成、绑定/字体校验、资源与泄漏诊断（仅 Editor） |
| `com.yato.input` | `Yato.Input` | Unity Input System | 可选 D07：运行时上下文、订阅、重绑定 |
| `com.yato.flow` | `Yato.Flow` | Core、App、资源抽象 | 异步流程与状态资源清理 |
| `com.yato.config` | `Yato.Config` | Core、资源抽象 | ScriptableObject 配置及引用校验 |
| `com.yato.pooling` | `Yato.Pooling` | Core、资源抽象 | 有界实例池、零分配值句柄 |
| `com.yato.save` | `Yato.Save` | Core、Persistence | 可选槽位存档、显式备份恢复 |
| `com.yato.ui.input` | `Yato.UI.Input` | UI、Input | 可选 UGUI 输入与焦点适配 |
| `com.yato.framework` | — | 上面全部（不含 input） | 聚合包：示例 `Samples~`、`Tests`、`Documentation~` |

## 依赖规则

1. `Yato.Core` 不引用 `UnityEditor`，不引用任何 `Yato.*` 程序集；
2. `Yato.UI` 只引用 Core / Mvc / `Yato.Resources.Abstractions`，不引用资源实现；
3. `Yato.Resources` 不引用 UI；
4. `Yato.Persistence` 不引用 Resources / UI；`Yato.Settings` 只在 Persistence 与 Core 之上；
5. `Yato.Audio` 不引用 Settings（音量由游戏层把设置转成 `SetBusVolume` 调用）；
6. `Yato.App` 不引用 UI；
7. 业务 / 示例程序集单向引用框架，框架永不反向引用；
8. 编辑器代码只存在于 `com.yato.editor`（`includePlatforms: ["Editor"]`）；
9. 不安装 `com.yato.input` 时，其余包仍能独立编译运行。

## 目录

```
Packages/
├── com.yato.core/            Runtime/  Yato.Core
├── com.yato.mvc/             Runtime/  Yato.Mvc
├── com.yato.resources/       Runtime/  Yato.Resources + Runtime/Abstractions（子 asmdef）
├── com.yato.ui/              Runtime/  Yato.UI
├── com.yato.app/             Runtime/  Yato.App（Bootstrap/ + Scene/）
├── com.yato.persistence/     Runtime/  Yato.Persistence
├── com.yato.settings/        Runtime/  Yato.Settings
├── com.yato.audio/           Runtime/  Yato.Audio
├── com.yato.editor/          Editor/   Yato.Editor
├── com.yato.input/           Runtime/ Yato.Input（可选）
└── com.yato.framework/       package.json（聚合）+ Tests/ + Samples~/ + Documentation~/
```

每个运行时包根目录都有 `link.xml`，只保留自己的程序集；单独安装某个包时不会丢掉 IL2CPP 裁剪保护。

安装、独立样例与 0.2 → 0.3 迁移见 [安装指南](Documentation~/installation.md)。Bundle 模式默认严格失败；本地下载需显式测试授权，旧 Provider 固定旧快照。

## 快速开始

```csharp
// 1) 注册全局服务（必须早于 Bootstrap，例如 RuntimeInitializeOnLoadMethod(BeforeSceneLoad)）
GameBootstrap.RegisterService(new MySaveService());

// 2) 初始化：幂等；失败会回滚已完成的服务并抛 AppInitializationException
GameBootstrap.Initialize();

// 3) 场景：忙时立即返回 Busy，不排队
IAwaitable<SceneLoadResult> run = GameBootstrap.Scene.LoadAsync(
    new SceneLoadRequest("Gameplay").WithInitializer(new GameplaySceneInitializer()));
run.Completed += () => Debug.Log(run.GetResult());

// 4) 设置：默认值 → 用户文件 → 校验 → 发布；显式保存
var settings = new SettingsService<MySettings>(new MySettings(), new FileStore(), "settings/game.json");
settings.Load();
settings.Apply(s => s.Volume = 0.6f);
settings.Save();

// 5) 音频：音量由游戏层从设置同步
audio.SetBusVolume(AudioBus.Music, settings.Current.MusicVolume);
```

## 测试

- EditMode：资源句柄/缓存/持久化/设置等纯逻辑与文件 IO 用例；
- PlayMode：UI 生命周期与并发、App/Scene 集成、音频池与渐变；
- 包内测试需在宿主工程 `Packages/manifest.json` 的 `testables` 中登记 `com.yato.framework`，否则 Unity 不会编译包内测试程序集。

## Unity 版本

| 版本 | 状态 |
|---|---|
| Unity 6（6000.0 起） | 唯一支持的 Unity 大版本；各包 `unity` 字段为 `6000.0` |
| 6000.5.6f1（Unity 6.5） | 当前开发与实测版本；其他 Unity 6 小版本不据此宣称已经实测 |

Unity 6 下已知差异：

- 引擎新增 `UnityEngine.AwaitableCompletionSource<T>` 与本包 `Yato.Resources.AwaitableCompletionSource<T>` 同名，在 `Yato.Resources` 命名空间之外引用时必须写全限定名；
- 本工程已移除 `com.unity.collab-proxy`（`TreeView` 废弃）与 `com.unity.timeline 1.7.6`（`GetInstanceID` 废弃），需要 Timeline 请安装 1.8.x；
- `com.unity.textmeshpro` 与 `com.unity.ugui` 暂保持原版本；升级 ugui 2.0 时 TMP 会被并入，记得同步移除 TMP 依赖。

## 编译符号

| 符号 | 作用 |
|---|---|
| `YATO_USE_BUNDLE` | 强制走 AssetBundle（编辑器下也读 AB） |
| `YATO_UI_ASYNC` | UI 异步打开路径 |
| `YATO_VERBOSE` | 打开 Debug 级日志 |

## 状态

| 里程碑 | 状态 |
|---|---|
| 基线稳定（§5.1 三条风险线索） | 已完成：多持有者引用计数、取消/失败回滚、缓存页所有权、同页并发单飞、Domain Reload 静态重置 |
| 包拆分 | 已完成：9 个运行时/工具包 + 聚合元包 |
| D01 平台优先级 | 冻结：Windows Player 优先 |
| D02 初始化与生命周期 | 已实现：`GameBootstrap` |
| D03 场景系统 | 已实现：`SceneService` + `SceneContext` |
| D04 持久化基础设施 | 已实现：`FileStore` / `PersistencePaths` / `ISerializer` |
| D05 设置管理 | 已实现：`SettingsService<T>` |
| D06 音频管理 | 已实现：`AudioService` |
| D07 输入适配 | 可选薄适配已实现；上下文、重绑定、订阅和 JSON 接口 |
| D08 工程与发布 | 持续进行：独立安装、样例与本机 Player 验证见 Documentation~/verification.md；人工交互与正式发布仍待验收 |

各阶段手动验证步骤见 `docs/YatoFramework_ManualVerification_2026-10-31.md`。
