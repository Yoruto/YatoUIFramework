# 文档索引

更新日期：2026-11-05（Asia/Shanghai）。当前框架版本为 `0.6.1`，Survivor 游戏版本为 `0.1.0`，开发与实测环境为 Unity `6000.5.6f1`。最新未发布修复见框架版本记录。

## 使用与开发

| 入口 | 内容 |
|---|---|
| [仓库说明](../README.md) | 当前状态、包结构和快速开始 |
| [安装与升级](EMBEDDED_INSTALL_AND_UPGRADE.md) | 完整 / 精简 / 可选包集及消费工程 |
| [框架接入指南](../Packages/com.yato.framework/Documentation~/index.md) | API 与各模块的使用方式 |
| [0.6 接入指南](../Packages/com.yato.framework/Documentation~/foundation06.md) | 异步适配、诊断、向导与预检 |
| [Survivor 手册](../Assets/SurvivorSample/README.md) | 运行、操作、资产重建、构建与移植 |
| [Unity 官方 MCP 配置记录](UNITY_OFFICIAL_MCP_SETUP.md) | 本机工具安装与已验证版本配套；其他电脑需要调整路径 |

## 交付与验证

| 记录 | 阅读说明 |
|---|---|
| [生命周期与异步修复验证](verification/ASYNC_LIFECYCLE_2026-11-05.json) | 2026-11-05 源工程回归、消费工程与原始证据摘要 |
| [Survivor 实施记录](SURVIVOR_IMPLEMENTATION_2026-11-01.md) | 已按最终日志更新；含完整战斗、独立构建、压力测量与未验收项目 |
| [Survivor 验证摘要](verification/SURVIVOR_2026-11-01.json) | 从本地原始日志提取的结果及 SHA-256，方便随仓库查看 |
| [提交前验证](verification/PRECOMMIT_2026-11-02.json) | 2026-11-02 当前工作区的检查结果 |
| [0.6 实施记录](FRAMEWORK_06_IMPLEMENTATION_2026-11-01.md) | 框架阶段的历史验收；其中 Survivor 延期和旧场景问题已由后续示例工作更新 |
| [0.5 实施记录](FOUNDATION_IMPLEMENTATION_2026-11-01.md) | 基础模块与独立消费工程的历史验收 |
| [优化验证记录](OPTIMIZATION_VERIFICATION_2026-10-31.md) | 更早的资源、生命周期与发布门槛验证 |
| [框架版本记录](../Packages/com.yato.framework/CHANGELOG.md) | 框架包的版本变更；Survivor 在 Assets 下独立交付 |

`Logs/`、`Build/` 和独立消费工程属于本机验证或构建产物，不纳入 Git；提交的 JSON 摘要保留原始日志名称与哈希，不代替完整日志。历史实施记录按当时范围阅读，当前 Survivor 状态以其实施记录为准。

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
