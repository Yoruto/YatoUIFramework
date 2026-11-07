# 文档索引

更新日期：2026-11-07（Asia/Shanghai）。当前框架版本为 `0.7.0`，Survivor 游戏版本为 `0.1.0`，开发与实测环境为 Unity `6000.5.6f1`。资源基础、Bundle 和热更新可独立安装，迁移见安装指南。

## 使用与开发

| 入口 | 内容 |
|---|---|
| [仓库说明](../README.md) | 当前状态、包结构和快速开始 |
| [安装与升级](EMBEDDED_INSTALL_AND_UPGRADE.md) | 完整 / 精简 / 可选包集及消费工程 |
| [UPM 模块包与离线安装](UPM_DISTRIBUTION.md) | 0.7.0 模块 TGZ / ZIP 导出、安装器与 ResourcesOnly 等按需组合 |
| [框架接入指南](../Packages/com.yato.framework/Documentation~/index.md) | API 与各模块的使用方式 |
| [0.6 接入指南](../Packages/com.yato.framework/Documentation~/foundation06.md) | 异步适配、诊断、向导与预检 |
| [Survivor 手册](../Assets/SurvivorSample/README.md) | 运行、操作、资产重建、构建与移植 |
| [Unity 官方 MCP 配置记录](UNITY_OFFICIAL_MCP_SETUP.md) | 本机工具安装与已验证版本配套；其他电脑需要调整路径 |

## 交付与验证

| 记录 | 阅读说明 |
|---|---|
| [URP 迁移记录](verification/URP_MIGRATION_2026-11-07.json) | 源工程/Survivor 的 URP 17.5.0、2D Unlit 配置及迁移后视觉证据；与下方历史结果分开阅读 |
| [Live Unity 验证](verification/LIVE_UNITY_2026-11-07.json) | 0.7 现场重跑 315 个测试、10 项冒烟、Survivor 交互与清理快照，保留脚手架错误及验收边界 |
| [UPM 归档安装验证](verification/UPM_DISTRIBUTION_2026-11-07.json) | ZIP/TGZ 三个独立消费工程的 6 项加载测试、12 项安装器检查及内容哈希 |
| [资源模块拆分验证](verification/MODULAR_RESOURCES_2026-11-07.json) | 0.7：五种独立包集、源工程回归、仅资源 Player 及程序集检查 |
| [生命周期与异步修复验证](verification/ASYNC_LIFECYCLE_2026-11-05.json) | 2026-11-05 源工程回归、消费工程与原始证据摘要 |
| [Survivor 实施记录](SURVIVOR_IMPLEMENTATION_2026-11-01.md) | 已按最终日志更新；含完整战斗、独立构建、压力测量与未验收项目 |
| [Survivor 验证摘要](verification/SURVIVOR_2026-11-01.json) | 从本地原始日志提取的结果及 SHA-256，方便随仓库查看 |
| [提交前验证](verification/PRECOMMIT_2026-11-02.json) | 2026-11-02 当前工作区的检查结果 |
| [0.6 实施记录](FRAMEWORK_06_IMPLEMENTATION_2026-11-01.md) | 框架阶段的历史验收；其中 Survivor 延期和旧场景问题已由后续示例工作更新 |
| [0.5 实施记录](FOUNDATION_IMPLEMENTATION_2026-11-01.md) | 基础模块与独立消费工程的历史验收 |
| [优化验证记录](OPTIMIZATION_VERIFICATION_2026-10-31.md) | 更早的资源、生命周期与发布门槛验证 |
| [框架版本记录](../Packages/com.yato.framework/CHANGELOG.md) | 框架包的版本变更；Survivor 在 Assets 下独立交付 |

`Logs/`、`Build/` 和独立消费工程属于本机验证或构建产物，不纳入 Git；提交的 JSON 摘要保留原始日志名称与哈希，不代替完整日志。历史实施记录按当时范围阅读；较早文档中的“未验收”以其日期为界，已补充验证的部分见下方及当前 0.7 验证摘要。

### 2026-11-07 源工程渲染迁移

为处理本机 Unity Hub 在 Unity 6.5 下显示的 Built-In 弃用提示，源工程与 Survivor 采用 **URP 17.5.0 + 2D Renderer / Unlit**，资产统一放在 `Assets/SurvivorSample/Rendering`。Survivor 消费工具读取源 manifest 的精确 URP 版本，并复制 GraphicsSettings/QualitySettings 与示例渲染资产，保持开发渲染基线一致；不复制整个 ProjectSettings 或本机 Pipeline/MCP。

18 个通用 UPM 模块保持渲染管线无关，普通模块安装不强制引入 URP。旧 Bundle 和 Player 需重建；下方已有测试和 Built-In 现场会话不证明 URP 渲染通过，迁移后的可见 Editor、Bundle 和 Player 证据单独见 [URP 记录](verification/URP_MIGRATION_2026-11-07.json)。

### 2026-11-07 UPM 模块分发

导出 18 个标准 UPM TGZ 和带安装器的离线 ZIP。ZIP 两包资源模式、ZIP 15 包完整模式及 TGZ 两包资源模式在独立 Unity 6000.5.6f1 工程完成解析、编译和加载/释放，**6 / 6** 通过；五种包集及拒绝覆盖、依赖版本、路径等安装器检查 **12 / 12** 通过。使用与校验方式见[分发指南](UPM_DISTRIBUTION.md)，已验证包内容以[归档安装验证](verification/UPM_DISTRIBUTION_2026-11-07.json)的 `packageSourceSha256` 匹配。归档保留模块元数据与示例，不包含源工程 Survivor 素材或本机 Unity MCP。

### 2026-11-07 Live Unity 现场验证

本机 Windows Unity `6000.5.6f1` 通过官方 Pipeline 重跑源工程 EditMode **195 / 195**、PlayMode **120 / 120**，合计 **315 / 315**，零失败、零跳过；基础冒烟 **7 项**和 Bundle 冒烟 **3 项**通过。这是对当前源工程的再次执行，不与下方同规模的拆包回归相加计算。

Survivor 默认 `EditorAssetProvider` 会话覆盖菜单、设置滑条与保存、战斗/升级、暂停与嵌套设置、恢复、结算重开和退出。暂停嵌套期间三次快照的战斗计时保持不变，暂停 token 为 **1 → 2 → 1**；恢复后计时增长，Retry 获得新的 RunId。资源与订阅随页面关闭回到基线。另在全新 Play 会话以公开动作验证 `BundleProvider` 的菜单、战斗/升级、结算与退出；最终控制台零错误、零警告。两类 Provider 退出后的实测项是 **tracked handles、loaded bundles、UI roots 均为零**，未将这些结果扩大解释为销毁后所有统计项都已采样。

冒烟中 EventBus 故意抛出一次异常来验证监听隔离，其 Error/Exception 日志属于预期；早期 Bundle 验证脚手架的错误类型转换已保留并从最终干净会话中排除。现场记录未证明 Esc/真实键盘或手柄输入、听音验收，也不代表移动端/WebGL、IL2CPP 或生产 HTTPS 已通过。详见 [Live 验证摘要](verification/LIVE_UNITY_2026-11-07.json)。

### 2026-11-07 按需资源模块

资源基础、Bundle 和 HotUpdate 已拆为独立包。源工程 EditMode **195 / 195**、PlayMode **120 / 120** 通过，零失败、零跳过；五个消费工程实际安装的 Yato 包数分别为 **2 / 4 / 3 / 12 / 15**，对应仅资源、精简 UI、本地 Bundle、离线完整、完整聚合。各组合的加载测试与完整/离线示例均通过；真实 AB 打包入口通过。

两包 ResourcesOnly 工程的 Windows Mono 构建和原生资源加载/释放冒烟通过，成品仅含 `Yato.Core`、`Yato.Resources`、`Yato.Resources.Abstractions` 三个 Yato 程序集，不包含 Bundle、HotUpdate 或 Persistence。初始化迁移见[安装指南](EMBEDDED_INSTALL_AND_UPGRADE.md)，日志哈希和初次失败修正见本次验证摘要。移动端/WebGL 与 IL2CPP 仍需专项验收。

### 2026-11-05 生命周期与异步修复

源工程 EditMode **189 / 189**、PlayMode **115 / 115** 通过，零失败、零跳过。覆盖后台取消和完成通知、资源关闭和迟到结果、共享 Bundle 取消隔离、真实 WebRequest 中止、路由重置及重入、示例重开监听、结算取消、Settings 并发和池回调。独立消费测试 **4 / 4**、Domain/Scene Reload 矩阵 **8 / 8**、Windows Mono Player 构建及完整冒烟均通过。资源所有权、预加载失败与 Settings 过期结果的兼容语义见 [0.6 接入指南](../Packages/com.yato.framework/Documentation~/foundation06.md)。消费工程、构建与原始日志哈希详见本次验证摘要；历史验证结果仍按原日期阅读。

### 2026-11-02 提交前检查

当时工作区 Unity EditMode **150 / 150**、PlayMode **101 / 101** 通过，零失败、零跳过。首次 PlayMode 运行发现 3 项测试夹具问题：虚拟输入与应用焦点依赖可见 Game 视图，以及旧热更冒烟固定假设只有一个 Bundle。现已在示例测试中显式设置并恢复焦点策略，等待真实移动更新；热更测试按真实版本清单校验下载 / 跳过数量，修改版本时保留完整 Bundle 集合。该轮没有修改游戏或框架运行时实现。

原始测试 XML、失败尝试、修正后结果及测试源码哈希均记入[提交前验证摘要](verification/PRECOMMIT_2026-11-02.json)。本次还核对了文档本地链接、Unity GUID、Survivor `.meta` 与 49 张原始素材的 SHA-256；历史 Player 与压力结果仍按原采样日期记录。

## 计划与问题

| 文档 | 内容 |
|---|---|
| [Survivor 开发计划](SURVIVOR_DEVELOPMENT_PLAN_2026-11-01.md) | 已实现首版的设计约束；计划中的故障注入不等于已执行 |
| [Survivor 框架问题](SURVIVOR_FRAMEWORK_ISSUES.md) | 接口限制、工具改进候选、示例适配与待复现风险 |
| [素材审计](SurvivorArtManifest.json) | 原图来源、哈希和切片；不参与运行时打包 |
| [稳定性与复用计划](YatoFramework_STABILITY_AND_REUSE_IMPROVEMENT_PLAN_2026-10-31.md) | 不含 CI 的框架改进范围 |
| [架构与路线图](YatoFramework_ARCHITECTURE_AND_ROADMAP_2026-10-30.md) | 架构决策与依赖约束 |
| [UI / MVC 可行性](UI_MVC_FEASIBILITY_2026-10-31.md) | UI 接入与生成链路评估 |
| [手动验证清单](YatoFramework_ManualVerification_2026-10-31.md) | 人工验收步骤 |

早期 [重构计划](FRAMEWORK-REFACTOR-PLAN.md) 与 [Spike 基线](SPIKE-BASELINE.md) 保留供追溯，不作为当前交付状态。
