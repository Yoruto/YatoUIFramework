# 框架完善实施记录（2026-11-01）

范围：仅 Unity 6，当前 6000.5.6f1。实施 A–D 的框架能力与通用验证；Yato Survivor 全部延期，不创建玩法、场景、配置或素材。

## 阶段 A

- 修正批处理打包结果检查，自检失败不复制产物。
- 新增 HotUpdateMaintenance.PrepareStartup；保留当前及上一成功快照，Loader/路径已使用时拒绝清理。与 RunAsync 共用互斥保护。
- EditMode：117 通过，0 失败，0 跳过，日志 Logs/Foundation-A-EditMode.xml。
- PlayMode：68 通过，0 失败，0 跳过，日志 Logs/Foundation-A-PlayMode.xml。

## 阶段 B

- 新增 Flow、Config 和可选 Save；各自独立 asmdef 与测试。
- 配置验证统一接入构建前，存档示例只包含计数器槽位。
- EditMode：127 通过，0 失败，0 跳过，日志 Logs/Foundation-B-EditMode.xml。
- PlayMode：72 通过，0 失败，0 跳过，日志 Logs/Foundation-B-PlayMode.xml。
- 曾修正 Unity 6 AwaitableCompletionSource 同名和测试中的嵌套异步等待；无产品同步降级。

## 阶段 C

- Pooling 值类型代次句柄、PauseService、ApplicationLifecycle、显式异步退出、独立页面事件与过渡、可选 UI/Input 已实现。
- PlayMode：88 通过，0 失败，0 跳过，日志 Logs/Foundation-C-PlayMode.xml。包含真实手柄输入事件提交、默认事件模块恢复、过渡后的焦点恢复与 1000 次预热取还零托管分配。
- EditMode：127 通过，0 失败，0 跳过，日志 Logs/Foundation-C-EditMode.xml。
- 曾发现并修正 UI/Input 恢复 Unity 自动默认动作所有权、过渡结束后焦点恢复。Windows 暂存目录移动被拒绝曾导致间歇性失败；最终在 D 阶段加入有界异步重试和确定性占用回归，之前失败不计为当轮通过。

## 阶段 D：独立验证与最终结果

| 检查 | 最终结果 | 证据 |
|---|---|---|
| 源工程 EditMode | 132 通过，0 失败，0 跳过 | Logs/Foundation-D-EditMode.xml |
| 源工程 PlayMode | 92 通过，0 失败，0 跳过 | Logs/Foundation-D-PlayMode.xml |
| 默认包集消费侧集成 | 2 通过，0 失败，0 跳过 | Logs/Foundation-Consumer-Full.xml |
| 可选模块消费侧目标测试 | 1 通过，0 失败，0 跳过 | Logs/Foundation-Consumer-Optional-Target.xml |
| 默认/精简/可选包集安装与实际配置资产 | PASS | Foundation-Consumer-Full/Minimal/Optional.log |
| 精简卸载后编译 | PASS（包移到 RemovedPackages，保留可逆备份） | Logs/Foundation-Consumer-Uninstall.log |
| Windows Mono Development 构建与冒烟 | PASS，退出码 0 | Logs/Foundation-Windows-Build.log / Foundation-Windows-Player.log |
| 可选焦点演示 Player 构建 | PASS | Logs/Foundation-Focus-Build.log |
| 可视窗口鼠标交互 | PASS：显示、打开/关闭弹窗与正常返回 | computer-use 窗口轨迹；独立窗口化非 Development Player |

目标回归共 **227 项通过**。可选消费工程曾不加过滤运行 91 项，其中 81 通过、10 个真实 AB 冒烟因该空工程不含原工程 AB 产物跳过；这些跳过没有计入目标通过数。源工程实际 AB 用例已运行通过。

消费侧对 FullSample 的 ready/idle 连续运行三轮，真实配置与实例池均可加载；状态退出后句柄回到 0，租用失效。Save CounterSample 与 UI/Input FocusSample 为独立通用演示，不含玩法。

性能记录（Mono、-batchmode -nographics、中性空实例）：poolPeak=8，created=8，预热后 1000 次取还 0 托管字节；60 帧平均 0.299 ms、最大 13.052 ms。此记录是本机空场景基线，不代表真实游戏或图形渲染性能。

最终额外修复：规范化缓存根目录尾部分隔符，阻止绕过使用保护；活动指针丢失但已有快照时保守保留，而不推断为可删除孤儿。两者有 EditMode 回归。缓存清理还覆盖链接预检、非法上一版本指针、锁定文件导致的清理失败和诊断计数。

热更提交额外修复：仅对未发生目录提升的 IO/权限失败进行最多 1 秒的非阻塞重试，每次间隔至少 50 ms；支持等待期间取消。活动指针原子提交失败立即返回，不重试、不降级覆盖。临时占用恢复、持续占用超时与取消三项 Windows 回归通过，最后完整 PlayMode 92/92 通过。

实际 CLI 负例：空消费工程执行 BuildBundlesWindows，进程返回 **1**，日志 Logs/Foundation-Bundle-Expected-Failure.log。预期失败不计为产品回归失败。

可视验证曾被 Windows 防火墙权限弹窗遮挡，由用户手动处理，未操作安全设置。鼠标打开/关闭弹窗通过；工具注入 Escape/Enter 未产生可观察效果，因此不标记实际键盘设备验证通过。独立 Input System 键盘取消和手柄提交用例通过；批处理键盘测试显式启用新设备，避免无焦点环境自动禁用设备。

## 未完成的环境验收

- 本机 Windows Standalone Support 仅有 Mono 变体，缺少 IL2CPP 支持模块；C++ 工具链未验证，IL2CPP 构建/运行不得标记通过。
- 实际键盘/手柄硬件、实际听音、生产 HTTPS 证书/网络行为仍需专项人工验收；音频逻辑及真实 AB 流程已通过自动测试。
- Yato Survivor 整体延期，未创建专属包、玩法、角色/敌人/武器配置、场景或占位素材。素材由用户提供后再开始。

## 提交

- 87cda8d：记录已有 Unity 6 / 0.3 基线，不包含 TMP 与项目设置改动。
- 8dfde06：阶段 A。
- 1512eb7：阶段 B。
- 9c11a3a：阶段 C。
- 阶段 D：本记录、独立验证、通用示例与文档整理随最后一次提交交付。
