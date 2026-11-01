最新 0.6 API 与接入工具见 [0.6 指南](foundation06.md)。

# 0.5 基础模块接入

仅支持 Unity 6，当前验证 6000.5.6f1。所有新模块均有独立 asmdef 和测试。游戏样例 Yato Survivor 延期，没有加入任何玩法内容。

## 安装组合

聚合包新增 Flow / Config / Pooling；完整嵌入式包集还需要原有 Core/Mvc/Persistence/Resources/UI/App/Settings/Audio/Editor。
Save、Input、UI.Input 仍按需安装；UI.Input 依赖 Input，基础 UI 不引用 Input 程序集。
包版本：Framework 0.5.0，Resources 0.2.1，App/UI/Editor 0.2.0；Flow/Config/Save/Pooling/UI.Input 0.1.0。保留的旧包版本不变。

## 启动顺序

在任何持久化资源 Loader/路径被使用之前显式调用 HotUpdateMaintenance.PrepareStartup()，检查 Success/Busy/InUse/Error 与清理指标。
它只清理受控版本目录，保护当前/上一版本和旧平铺缓存。指针缺失但已有快照、指针损坏或路径有链接时保留快照并报告。资源使用后，本次运行拒绝清理；不要把该入口当运行时垃圾回收。

然后按原有 GameBootstrap 注册/初始化服务，创建 FlowController，并通过 IFlowState.EnterAsync 串起异步加载。每个状态使用自己的 FlowContext.Resources。取消是协作式，等待旧任务真正结束才解除 Busy。失败不自动回滚，错误界面由调用方决定。

## 配置

定义可序列化的 ConfigRecord 子类和具体 ConfigTable<T> ScriptableObject 子类，ScriptableObject 的类名与文件名保持一致。
将表放进 ConfigCatalog；行的 Validate 使用 Report 或 Require<T>(id)。
ConfigService.LoadAsync(resources, catalogKey) 持有资源句柄，Get<T>/TryGet<T> 查询运行副本，Dispose 释放。
无效 ID/数值/跨表引用在运行加载、Editor 菜单、Player 与 AB 构建前统一拒绝。

## 存档（可选）

SaveService<T>(store, dataVersion, validate, migrator) 位于 Persistence 之上。加载结果区分 Missing/Loaded/Migrated/Corrupt/Incompatible/Failed，含 BackupAvailable。
Load 不创建文件或自动恢复；迁移结果只在内存。Save 对有效旧主文件先写备份，再原子提交新主文件；损坏或不兼容主文件拒绝直接覆盖。
显式 RecoverBackupAsync 进行回退；删除是两个文件的显式操作，不承诺跨文件原子性。业务 DTO、数据版本和迁移策略由游戏定义。
同根同槽位跨服务互斥；槽位 ID 使用安全字符。详细取消边界及 JSON 限制见 Save 包 README。

## 实例池和暂停

ResourceInstancePool.CreateAsync 持有一次预制体加载；Prewarm、TryRent、Dispose 管理实例生命周期。总容量满时拒绝生成。
PooledInstance 为带代次的 readonly struct，避免装箱后可用于零分配热路径；旧副本无法归还后续租用。通过场景 ResourceScope 管理池本身。
IPoolCallbacks 的状态重置由调用方负责，预制体建议初始 inactive；外部销毁后的补建允许分配。

PauseService.Acquire(reason) 返回暂停令牌；多层原因叠加，最后释放恢复原倍率。应用只设置一个时间倍率管理者。
ApplicationLifecycle 提供焦点、应用暂停和退出信号。ApplicationExitCoordinator 只在显式 RequestExitAsync 中等待已注册任务；失败/取消不退出，不承诺系统强杀时完成写入。

## UI / Input（可选）

UIRouter.PageOpened/PageClosed/TopPageChanged 不依赖 Facade。UIPageFocus 定义输入接管、默认选中和 AllowBack。
UIPageTransition 启用时淡入，OpenAsync 保持激活完成语义；CloseAsync 等待淡出，取消保留页面，原 Close 立即关闭。过渡用非缩放时间，结束后协调器恢复可交互焦点。
UIInputCoordinator 使用 InputContextService.Actions 副本与 InputSystemUIInputModule，配置路径和 map 名均由调用方提供。
先释放协调器，再释放输入服务。项目 Active Input Handling 选择 Input System 或 Both；本机可选消费工程准备工具会设置 Both。

## 通用示例与验证工具

BasicSample 保持精简；FullSample 自动进入 ready 流程，加载真实配置资产并预热中性实例池，可停止/重启、取还实例、暂停。
Save 的 CounterSample 演示计数器槽位；UI.Input 的 FocusSample 演示真实 UGUI 弹窗、键鼠/手柄和焦点恢复。它们都有可打开场景，互不要求对方安装。
手动工具 Prepare-ConsumerProject.ps1 支持 -PackageSet Minimal|Full、-IncludeInput、-IncludeSave。两个可选开关同时开启时加入可选消费侧测试。
所有检查均为本机按需执行，无 CI。实际结果和未满足的 IL2CPP 等门槛见原仓库 docs/FOUNDATION_IMPLEMENTATION_2026-11-01.md。
