# YatoUIFramework

Yato 通用 Unity 框架（UPM 多包）：UI 路由与生命周期、资源与热更、启动与场景、持久化、设置、音频，以及编辑器工具链。

当前状态：`0.2.0`（聚合包 `com.yato.framework`），D01–D06 已实现，D07 输入适配为占位包，D08 发布回归待执行。

## 文档

| 文档 | 内容 |
|---|---|
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
├── com.yato.input/         （占位，D07 待审核）
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

- Unity 2022.3 LTS 起（`package.json` 的 `unity` 字段），本工程验证于 6000.5.6f1；
- 包内测试需在 `Packages/manifest.json` 中登记 `"testables": ["com.yato.framework"]`；
- 所有包均为内嵌包（直接放在 `Packages/` 下），无需写入 manifest 依赖。
