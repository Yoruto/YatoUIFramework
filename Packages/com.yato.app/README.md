# Yato App

GameBootstrap/SceneService 的原有设计保持不变。

- PauseService.Acquire(reason) 返回可释放暂停令牌，多原因叠加；最后一个释放后恢复首次暂停前的 Time.timeScale。单个应用使用一个实例管理时间倍率，暂停期间不要直接从外部改写倍率。可选 pauseOnFocusLoss 监听焦点；Dispose 退订并恢复倍率。
- ApplicationLifecycle 提供 FocusChanged/PauseChanged/Quitting 信号，观察者异常隔离，SubsystemRegistration 清理静态订阅。
- ApplicationExitCoordinator.Register(async token => ...) 注册退出前任务；RequestExitAsync 按注册快照顺序等待，Busy 时返回 false。保存失败/取消不执行 Shutdown 或 Quit。全部成功后调用 GameBootstrap.Shutdown 与退出动作。测试可注入退出动作。Dispose 取消在途退出，等待任务实际结束才释放 Busy。

退出任务属于显式协作流程，不保证 OS 强杀或 OnApplicationQuit 中完成异步写入。以上接口在 Unity 主线程调用。
