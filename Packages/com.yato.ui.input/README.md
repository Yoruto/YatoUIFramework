# Yato UI/Input

独立可选包。new UIInputCoordinator(inputService, eventSystem, baseMap, uiMap, bindings) 复用 InputContextService.Actions 副本，不拥有传入服务与 EventSystem。UIInputBindings 的路径可配置；Point/Click/Navigate/Submit/Cancel 必需，Scroll 可选。

给需要接管输入的页面根节点挂 UIPageFocus，设置 DefaultSelected 和 AllowBack。页面关闭后恢复上一页选中对象，全部关闭后恢复基础上下文；SetBaseContext 可在流程切换时改变基础 map。已有 UI 模块配置和其他模块的启用状态在 Dispose 时恢复；由适配器创建的模块由它销毁。先 Dispose 协调器，再 Dispose 输入服务。

UIRouter 新增 PageOpened/PageClosed/TopPageChanged，和 Facade 是否存在无关。同步 Close 保持立即关闭。UIPageTransition 在启用时自动淡入，OpenAsync 仍在页面激活后完成；CloseAsync 等待可选淡出，取消则保留页面。所有过渡使用非缩放时间。

Samples/FocusSample 为通用 UGUI 焦点与弹窗演示，不包含玩法。
