# Yato Input（占位包，未实现）

> 状态：**占位**。本包不包含任何程序集或代码，仅用于固定包名与依赖位置。
> 设计依据：`docs/YatoFramework_ARCHITECTURE_AND_ROADMAP_2026-10-30.md` 的 D07（推荐默认方案，尚待整体审核）。

## 为什么是空的

D07 属于「待整体审核」的推荐方案，且明确要求「可选模块真正可选」：
不安装本包时，Core / Scene / Persistence 必须仍能编译运行。
在方案确认前先落地代码，会迫使其它包反向依赖输入系统，违背依赖约束。

## 计划范围（审核通过后实现）

- `InputActionAsset` / ActionMap / 绑定由游戏定义，框架只提供统一启用、关闭与上下文切换的辅助服务；
- 常见上下文（Gameplay / UI）只是示例，不硬编码为框架枚举；
- 运行时 Rebinding、取消、清理 `RebindingOperation`、恢复默认绑定；
- 复用官方 `SaveBindingOverridesAsJson` / `LoadBindingOverridesFromJson`，文件保存交给 Settings / 游戏与 `Yato.Persistence`；
- 订阅必须有明确持有者，场景卸载 / 服务关闭后不遗留回调；
- 独立 asmdef，对 `com.unity.inputsystem` 为**可选依赖**（未安装时整个程序集不参与编译）。

首版不做：直接封装 `GetKeyDown/GetAxis`、复杂本地多人协调、平台专属震动或设备图标库、输入录制/回放。

## 安装到工程

本包目前不会被 `com.yato.framework` 依赖。等 D07 实现后：

1. 工程已安装 `com.unity.inputsystem`；
2. 在 `Packages/manifest.json` 的 `dependencies` 或通过 Package Manager 添加 `com.yato.input`；
3. 未安装输入系统的工程不要添加该包（包内 asmdef 会用 `versionDefines` 做条件编译）。
