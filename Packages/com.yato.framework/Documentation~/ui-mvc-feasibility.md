# UI / PureMVC 可选解耦审核

结论：本轮保留当前包结构。现有路由已支持无 Facade 的页面，不需要为这一用法拆分 UI Core / MVC Adapter。若第二个独立项目确需其他 UI 架构，再评估程序集拆分。

本轮的 `BasicSample` 和独立消费工程验证使用：
```csharp
UIRouter.Configure(resources); // facade = null
var page = new UIPageDef("Basic", null, new AssetKey("yato/basic/page"));
UIRouter.OpenAsync(page);
```
预制体挂 UIPageView。UIPageFactory 在没有 MediatorType 时直接将 View 作为 IUIPage 驱动；UIRouter 的注册/通知代码在 Facade 为空时跳过。无需业务 Facade、Mediator、Proxy。由于 Configure 的公开签名仍含 IFacade，调用方 asmdef 仍必须显式引用 Yato.Mvc（可省略参数不消除此编译依赖）；本轮新示例均已声明。

| 边界 | 当前影响 | 拆分影响 |
|---|---|---|
| UIRouter.Configure / Facade 属性 | 签名引用 IFacade，但参数可省略 | 改公开 API 或注入通知适配器 |
| UIPageDef.MediatorType | 可为 null | 可保留 Type 或移出 MVC 配置 |
| UIPageContext 的 Mediator/Proxy | 直接引用 UIPageMediator/UIPageProxy | 需要拆上下文或扩展字段 |
| UIPageFactory | 创建、命名、绑定 Mediator/Proxy | 分离构造工厂与页面生命周期 |
| UIPageMediator / UIPageProxy | 继承 PureMVC 类型 | 移动到独立 Adapter 程序集并迁移 GUID/命名空间 |
| Yato.UI.asmdef / package.json | 始终依赖 Mvc | 拆包后可移除；增加安装与测试组合 |

方案取舍：保留现状维护成本最低；可选适配器仍需处理工厂/上下文扩展；完整拆包涉及公开契约、反射创建和序列化迁移。本轮收益主要是去掉一个小型源码包依赖，尚无第二个真实项目证明收益超过维护成本。没有强制重构。
