# YatoUIFramework

Yato 通用 Unity 框架（UPM 多包）：UI 路由与生命周期、资源与热更、启动与场景、持久化、设置、音频，以及编辑器工具链。

当前状态（2026-11-07）：框架 `0.7.0`，Yato Survivor（The Last Watch）示例 `0.1.0`。资源基础、AssetBundle 和资源热更新已拆成独立包；离线游戏无需安装 Bundle 或热更包。框架提供 Task/IAwaitable 桥接、统一诊断、项目接入向导与构建预检；Survivor 已实现十分钟战斗、三类武器、升级、设置与历史记录。

Unity `6000.5.6f1` 现场重跑 EditMode **195 / 195**、PlayMode **120 / 120**，共 **315** 个测试通过，另有 **7 项基础 + 3 项 Bundle 冒烟**通过。Windows Editor 的 Survivor 设置、嵌套暂停、恢复、结算重开及退出清理已有现场证据；BundleProvider 的干净会话另经公开动作验证。独立包集和 Resources-only Mono Player 结果见[拆包验证](docs/verification/MODULAR_RESOURCES_2026-11-07.json)，现场记录及边界见[Live Unity 验证](docs/verification/LIVE_UNITY_2026-11-07.json)。移动端/WebGL、IL2CPP、真实键盘/手柄及听音验收仍未完成。

## 文档

| 文档 | 内容 |
|---|---|
| [完整文档索引](docs/README.md) | 使用指南、当前交付、历史验收与计划的阅读入口 |
| [0.7 按需资源模块验证](docs/verification/MODULAR_RESOURCES_2026-11-07.json) | 五种独立包集、真实 Bundle 构建、Resources-only Player 与程序集检查 |
| [Live Unity 验证](docs/verification/LIVE_UNITY_2026-11-07.json) | 315 个测试、10 项冒烟、Survivor 会话快照及未验收边界 |
| [Survivor 运行与开发手册](Assets/SurvivorSample/README.md) | 操作、游戏入口、重建、验证与移植 |
| [Survivor 实施记录](docs/SURVIVOR_IMPLEMENTATION_2026-11-01.md) | 交付目录、框架接入、实测结果和验证范围 |
| [Survivor 示例开发计划](docs/SURVIVOR_DEVELOPMENT_PLAN_2026-11-01.md) | 玩法范围、框架接入约束、资源所有权、实施阶段与验收 |
| [Survivor 框架问题记录](docs/SURVIVOR_FRAMEWORK_ISSUES.md) | 接入过程中发现的限制、证据、当前处理与后续修补候选 |
| [Unity 官方 MCP 配置记录](docs/UNITY_OFFICIAL_MCP_SETUP.md) | 本机开发连接、版本配套与路径配置 |
| [0.6 框架实施记录](docs/FRAMEWORK_06_IMPLEMENTATION_2026-11-01.md) | 异步桥接、诊断、接入与生命周期验收 |
| [0.5 框架实施记录](docs/FOUNDATION_IMPLEMENTATION_2026-11-01.md) | 分阶段提交、测试证据、性能与待验收项 |
| [最新优化计划](docs/YatoFramework_STABILITY_AND_REUSE_IMPROVEMENT_PLAN_2026-10-31.md) | 稳定性与复用优化范围（不含 CI） |
| [安装与升级](docs/EMBEDDED_INSTALL_AND_UPGRADE.md) | 完整/精简包集、独立示例 Quick Start、兼容性迁移 |
| [UPM 模块包与离线 ZIP](docs/UPM_DISTRIBUTION.md) | 导出 18 个模块 TGZ、离线套件，按包集安装到其他项目 |
| [0.3 历史验证记录](docs/OPTIMIZATION_VERIFICATION_2026-10-31.md) | 当时的实测证据与待验收项；当前状态见 0.7 两份验证摘要 |
| [架构与路线图](docs/YatoFramework_ARCHITECTURE_AND_ROADMAP_2026-10-30.md) | D01–D08 设计决策、依赖约束、里程碑 |
| [手动验证清单](docs/YatoFramework_ManualVerification_2026-10-31.md) | 各阶段验收步骤（含 §5.2 最小集成流程） |
| [包说明 / 快速开始](Packages/com.yato.framework/README.md) | 包结构、依赖规则、按需组合 |
| [详细接入指南](Packages/com.yato.framework/Documentation~/index.md) | UI / 资源 / 场景 / 设置 / 音频 / 热更 |
| [变更记录](Packages/com.yato.framework/CHANGELOG.md) | 版本变更 |

## 包结构

```
Packages/
├── com.yato.core/          Yato.Core          运行时基础（RuntimeHost / 事件 / 服务定位 / 日志）
├── com.yato.mvc/           Yato.Mvc           PureMVC
├── com.yato.resources/     Yato.Resources    基础资源、Resources/Editor Provider、句柄和诊断
├── com.yato.resources.bundle/ Yato.Resources.Bundle 可选 AssetBundle 后端与打包工具
├── com.yato.resources.hotupdate/ Yato.Resources.HotUpdate 可选资源下载、版本和快照更新
├── com.yato.ui/            Yato.UI           页面路由、层级、缓存、绑定
├── com.yato.app/           Yato.App          GameBootstrap + SceneService
├── com.yato.persistence/   Yato.Persistence  安全文件读写与序列化
├── com.yato.settings/      Yato.Settings     强类型设置与变化通知
├── com.yato.audio/         Yato.Audio        通道音量、音乐渐变、SFX 池
├── com.yato.editor/        Yato.Editor       向导 / 代码生成 / 校验 / 诊断（仅 Editor）
├── com.yato.flow/         Yato.Flow         异步状态转换与资源范围
├── com.yato.config/       Yato.Config       配置目录、类型查询与统一校验
├── com.yato.pooling/      Yato.Pooling      资源实例池与代次句柄
├── com.yato.save/         Yato.Save         可选槽位存档、显式恢复、迁移
├── com.yato.ui.input/     Yato.UI.Input     可选 UGUI/Input 上下文与焦点
├── com.yato.input/         Yato.Input        可选 Input System 上下文与重绑定
└── com.yato.framework/     聚合包 + Tests + Samples~ + Documentation~
```

依赖只向下：`Core → Resources → UI`，`Core + Persistence → Settings`，`Core + Resources → Audio`，`Core + Resources(Abstractions) → App`；Editor 不进 Player。

## 按需安装

| 游戏需求 | Yato 包集 |
|---|---|
| 仅本地资源 | Core、Resources |
| 基础 UI | Core、Resources、Mvc、UI |
| 本地 AssetBundle，无热更 | 在已有包集上增加 Resources.Bundle |
| 资源热更新 | 增加 Resources.HotUpdate 及其 Persistence 依赖 |

`com.yato.framework` 是全量聚合包，包含 Bundle 与 HotUpdate。精简项目只复制所需子包及依赖到 `Packages/`；基础资源与 UI 不会带入热更。FullSample 已移除热更依赖，热更 GUI 演示从 HotUpdate 包单独导入。

```csharp
// Resources-only，业务统一持有并在退出时 Dispose。
var resources = ResourceServiceFactory.Create(new ResourcesProvider(), ownsProvider: true);
UIRouter.Configure(resources);
```

安装 `0.7.0` 时按[安装与升级指南](docs/EMBEDDED_INSTALL_AND_UPGRADE.md)选择包集，完整复制包目录及 `.meta`、`link.xml`。各子包独立版本，不应全部改成 `0.7.0`。从 0.6 升级时，将原资源工厂的 Bundle 初始化 API 改为 `BundleResourceServiceFactory`，移除 `ForceBundleMode`，并为相关 asmdef 添加 `Yato.Resources.Bundle` / `Yato.Resources.HotUpdate` 引用。两类扩展的公开类型仍使用 `Yato.Resources` 命名空间。

## 复用到其他项目

通过[分发指南](docs/UPM_DISTRIBUTION.md)导出标准 UPM 模块包和离线 ZIP：

```powershell
./tools/Export-UPMPackages.ps1
# 也可从仓库直接按需安装到已有的 Unity 6 项目
./tools/Install-YatoPackages.ps1 -ProjectPath E:/MyGame -PackageSet ResourcesOnly
```

默认导出到 `Build/Distribution/YatoFramework-0.7.0/` 下的新目录，包含 18 个模块 TGZ、`YatoFramework-0.7.0-offline.zip` 与 `release-manifest.json`。离线 ZIP 解压后自带安装器和包目录，可选 ResourcesOnly、MinimalUI、LocalBundles、OfflineFull 或 Full，自动补齐 Yato 依赖，不改写目标 `manifest.json`。产物及其实际哈希以导出清单为准；二进制归档保存在被 Git 忽略的 Build 目录，GitHub 提交源码、文档和可重现工具。

## 快速开始

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void Register() => GameBootstrap.RegisterService(new MyService());

// Bootstrap Scene 中的 GameBootstrap 组件（或编辑器自动补初始化）会按 Order 初始化服务
GameBootstrap.Scene.LoadAsync(new SceneLoadRequest("Gameplay"));
```

### Android / WebGL 的 Bundle 初始化

Android 和 WebGL 的 `StreamingAssets` 是 URL，不可直接用 `File.ReadAllText` 读取内置 `manifest.json`。
使用 Bundle 模式的游戏必须在初始化依赖资源的服务/UI 之前，等待异步工厂完成：

```csharp
var opening = BundleResourceServiceFactory.CreateAsync();
opening.Completed += () =>
{
    IResourceService resources = opening.GetResult();
    UIRouter.Configure(resources);
    // 随后创建需要资源服务的游戏对象；退出时 Dispose 此服务。
};
```

桌面平台和 Editor 的资源工厂仍可同步完成。`Create()` 保留为同步接口，适用于本地 manifest
或显式注入的 Provider；Android/WebGL 内置 Bundle 模式需要上述异步初始化。基于同步
`IAppService.Initialize()` 的项目需要调整启动顺序：在自动 Bootstrap 场景之前通过预加载入口
创建资源服务，再注入对应的游戏服务；仅调用旧版同步 `Create()` 不会自动等待 URL 请求。
独立演示入口 `GameStart` 已采用异步工厂。当前 Survivor 示例的自动 Bootstrap 路径仍以已验证的
Windows/Editor 为目标，迁移到 Android/WebGL 时也必须调整这一步。

## 环境

- 仅支持 Unity 6（`package.json` 的 `unity` 字段为 `6000.0`），当前开发与实测版本为 `6000.5.6f1`；
- 包内测试需在 `Packages/manifest.json` 中登记 `"testables": ["com.yato.framework"]`；
- 所有包均为内嵌包（直接放在 `Packages/` 下），无需写入 manifest 依赖。

## 运行 Survivor 示例

在 Unity 中打开 `Assets/SurvivorSample/Scenes/SurvivorBootstrap.unity` 后 Play，使用 WASD / 方向键移动，自动攻击，Escape 暂停。宿主工程输入模式为 Both，构建列表包含 Bootstrap 和 Arena。

通过 `Yato/Survivor/Build Windows Game` 生成 `Build/SurvivorWindows/TheLastWatch.exe`；本机整理好的运行目录为 `Build/TheLastWatch`。构建产物不纳入 Git，首次克隆后需要自行构建。独立移植可运行 `tools/Prepare-SurvivorConsumer.ps1 -Destination <新目录>`，详见[示例手册](Assets/SurvivorSample/README.md)。
