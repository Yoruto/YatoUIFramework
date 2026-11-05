# YatoUIFramework

Yato 通用 Unity 框架（UPM 多包）：UI 路由与生命周期、资源与热更、启动与场景、持久化、设置、音频，以及编辑器工具链。

当前状态（2026-11-05）：框架 `0.6.1`，Yato Survivor（The Last Watch）示例 `0.1.0`。框架提供 Task/IAwaitable 桥接、统一诊断、项目接入向导与构建预检；Survivor 已实现十分钟战斗、三类武器、升级、设置与历史记录，并通过独立 Windows Mono 构建。游戏首版接入未修改框架运行时与编辑器实现；后续修复与验证范围见实施记录。

## 文档

| 文档 | 内容 |
|---|---|
| [完整文档索引](docs/README.md) | 使用指南、当前交付、历史验收与计划的阅读入口 |
| [Survivor 运行与开发手册](Assets/SurvivorSample/README.md) | 操作、游戏入口、重建、验证与移植 |
| [Survivor 实施记录](docs/SURVIVOR_IMPLEMENTATION_2026-11-01.md) | 交付目录、框架接入、实测结果和验证范围 |
| [Survivor 示例开发计划](docs/SURVIVOR_DEVELOPMENT_PLAN_2026-11-01.md) | 玩法范围、框架接入约束、资源所有权、实施阶段与验收 |
| [Survivor 框架问题记录](docs/SURVIVOR_FRAMEWORK_ISSUES.md) | 接入过程中发现的限制、证据、当前处理与后续修补候选 |
| [Unity 官方 MCP 配置记录](docs/UNITY_OFFICIAL_MCP_SETUP.md) | 本机开发连接、版本配套与路径配置 |
| [0.6 框架实施记录](docs/FRAMEWORK_06_IMPLEMENTATION_2026-11-01.md) | 异步桥接、诊断、接入与生命周期验收 |
| [0.5 框架实施记录](docs/FOUNDATION_IMPLEMENTATION_2026-11-01.md) | 分阶段提交、测试证据、性能与待验收项 |
| [最新优化计划](docs/YatoFramework_STABILITY_AND_REUSE_IMPROVEMENT_PLAN_2026-10-31.md) | 稳定性与复用优化范围（不含 CI） |
| [安装与升级](docs/EMBEDDED_INSTALL_AND_UPGRADE.md) | 完整/精简包集、独立示例 Quick Start、兼容性迁移 |
| [本轮验证记录](docs/OPTIMIZATION_VERIFICATION_2026-10-31.md) | 实测证据与未完成的发布门槛 |
| [架构与路线图](docs/YatoFramework_ARCHITECTURE_AND_ROADMAP_2026-10-30.md) | D01–D08 设计决策、依赖约束、里程碑 |
| [手动验证清单](docs/YatoFramework_ManualVerification_2026-10-31.md) | 各阶段验收步骤（含 §5.2 最小集成流程） |
| [包说明 / 快速开始](Packages/com.yato.framework/README.md) | 包结构、依赖规则、编译符号 |
| [详细接入指南](Packages/com.yato.framework/Documentation~/index.md) | UI / 资源 / 场景 / 设置 / 音频 / 热更 |
| [变更记录](Packages/com.yato.framework/CHANGELOG.md) | 版本变更 |

## 包结构

```
Packages/
├── com.yato.core/          Yato.Core          运行时基础（RuntimeHost / 事件 / 服务定位 / 日志）
├── com.yato.mvc/           Yato.Mvc           PureMVC
├── com.yato.resources/     Yato.Resources    资源句柄、Provider、热更、诊断（含 Abstractions 子程序集）
├── com.yato.ui/            Yato.UI           页面路由、层级、缓存、绑定
├── com.yato.app/           Yato.App          GameBootstrap + SceneService
├── com.yato.persistence/   Yato.Persistence  安全文件读写与序列化
├── com.yato.settings/      Yato.Settings     强类型设置与变化通知
├── com.yato.audio/         Yato.Audio        通道音量、音乐渐变、SFX 池
├── com.yato.editor/        Yato.Editor       打包 / 代码生成 / 校验 / 诊断（仅 Editor）
├── com.yato.flow/         Yato.Flow         异步状态转换与资源范围
├── com.yato.config/       Yato.Config       配置目录、类型查询与统一校验
├── com.yato.pooling/      Yato.Pooling      资源实例池与代次句柄
├── com.yato.save/         Yato.Save         可选槽位存档、显式恢复、迁移
├── com.yato.ui.input/     Yato.UI.Input     可选 UGUI/Input 上下文与焦点
├── com.yato.input/         Yato.Input        可选 Input System 上下文与重绑定
└── com.yato.framework/     聚合包 + Tests + Samples~ + Documentation~
```

依赖只向下：`Core → Resources → UI`，`Core + Persistence → Settings`，`Core + Resources → Audio`，`Core + Resources(Abstractions) → App`；Editor 不进 Player。

## 快速开始

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void Register() => GameBootstrap.RegisterService(new MyService());

// Bootstrap Scene 中的 GameBootstrap 组件（或编辑器自动补初始化）会按 Order 初始化服务
GameBootstrap.Scene.LoadAsync(new SceneLoadRequest("Gameplay"));
```

## 环境

- 仅支持 Unity 6（`package.json` 的 `unity` 字段为 `6000.0`），当前开发与实测版本为 `6000.5.6f1`；
- 包内测试需在 `Packages/manifest.json` 中登记 `"testables": ["com.yato.framework"]`；
- 所有包均为内嵌包（直接放在 `Packages/` 下），无需写入 manifest 依赖。

## 运行 Survivor 示例

在 Unity 中打开 `Assets/SurvivorSample/Scenes/SurvivorBootstrap.unity` 后 Play，使用 WASD / 方向键移动，自动攻击，Escape 暂停。宿主工程输入模式为 Both，构建列表包含 Bootstrap 和 Arena。

通过 `Yato/Survivor/Build Windows Game` 生成 `Build/SurvivorWindows/TheLastWatch.exe`；本机整理好的运行目录为 `Build/TheLastWatch`。构建产物不纳入 Git，首次克隆后需要自行构建。独立移植可运行 `tools/Prepare-SurvivorConsumer.ps1 -Destination <新目录>`，详见[示例手册](Assets/SurvivorSample/README.md)。
