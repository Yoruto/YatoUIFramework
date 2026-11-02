# Yato Flow

Unity 6 的主线程异步流程层，位于 Bootstrap 之上。构造 FlowController 时传入 IFlowState 列表，以 SwitchAsync(id, token) 转换。状态通过 FlowContext.Resources 持有资源。

进入前取消保留旧状态；退出旧状态后进入失败不自动回滚。取消为协作式，在途 EnterAsync 结束前继续 Busy。Dispose 取消在途任务并在结束后清理。IAppService.Initialize 的同步语义保持不变。
