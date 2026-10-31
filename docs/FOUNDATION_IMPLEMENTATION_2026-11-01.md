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
- 曾发现并修正 UI/Input 恢复 Unity 自动默认动作所有权、过渡结束后焦点恢复。一次 Windows 暂存目录移动被拒绝的热更用例已在后续完整回归通过，保留其失败信息，不计为当轮通过。
