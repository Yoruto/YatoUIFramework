# 嵌入式安装、示例与升级（0.6.1）

仅支持 Unity 6，包最低声明为 6000.0，当前开发与实测为 6000.5.6f1。当前结果见 [实施记录](FRAMEWORK_06_IMPLEMENTATION_2026-11-01.md)，API 见 [0.6 指南](../Packages/com.yato.framework/Documentation~/foundation06.md)。

## 包集

复制所需 com.yato.* **整个目录，含 .meta 和 link.xml** 到消费工程 Packages。嵌入式包不需要在 manifest 重复登记同名依赖，不复制原工程 Assets、Library 或 ProjectSettings。

| 包 | 版本 | 直接 Yato 依赖 |
|---|---|---|
| Core | 0.2.0 | 无 |
| Persistence | 0.1.1 | Core |
| Resources | 0.3.1 | Core、Persistence |
| Mvc | 0.1.0 | 无 |
| UI | 0.3.0 | Core、Resources、Mvc |
| App | 0.3.0 | Core、Resources |
| Settings | 0.1.1 | Core、Persistence |
| Audio | 0.1.1 | Core、Resources |
| Flow | 0.2.0 | Core、App、Resources |
| Config | 0.2.0 | Core、Resources |
| Pooling | 0.2.0 | Core、Resources |
| Editor | 0.3.1 | Core、Resources、UI、Config、App |
| Framework | 0.6.1 | 上述默认包 |
| Input（可选） | 0.1.0 | 无；依赖 Unity Input System |
| Save（可选） | 0.2.0 | Core、Persistence |
| UI.Input（可选） | 0.2.0 | Core、UI、Input |

- 完整默认包集共 13 包，不含 Save/Input/UI.Input。
- 精简 UI 包集：Core、Persistence、Resources、Mvc、UI，共五包；BasicSample 再增加 App。
- FullSample 使用完整默认包集，新增 Flow/Config/Pooling，不要求可选模块。
- Save 的 CounterSample 和 UI.Input 的 FocusSample 在各自包中导入。
- 使用 Unity 6 UGUI。Editor 的旧 TMP 包声明可能出现弃用提示，TMP 功能由 UGUI 提供；基础包和新示例不引用 TMP 字体。
- 聚合包的 Git 子目录 URL 不会自动解析同仓库兄弟包。当前正式支持嵌入式安装，未发布 Registry。

## Quick Start

1. 新建 Unity 6 工程，建议 6000.5.6f1；复制需要的包集，等待编译。
2. Package Manager 导入 BasicSample，打开 Scenes/YatoBasic.unity，把它和 YatoBasicSecond.unity 加入构建场景。
3. Play，打开/关闭页面，切换 Single 场景，观察资源释放日志。
4. 导入 FullSample，打开 YatoFull.unity。ready 流程自动加载配置目录并预热中性实例池，可取还实例、停止/重启流程、暂停，以及演示设置和音频。
5. 可选导入 CounterSample，打开 CounterSample.unity；递增、保存两次、加载或显式恢复备份。
6. 可选导入 FocusSample，打开 FocusSample.unity，将 Active Input Handling 设为 Input System 或 Both；用鼠标、方向键/Enter 或手柄操作弹窗和返回。
7. 各示例单独运行；没有自动注册的未运行示例，也没有 Yato Survivor 内容。

## 升级与行为

- 各模块版本独立维护，整体功能版本为 Framework 0.6.1；替换完整包目录，保留 GUID，避免并排安装多个版本。
- 导入到 Assets 的样例是副本，升级不会自动覆盖。备份或移除旧副本后再导入，避免重复 GUID。
- Bundle 模式默认 BundleRequired；只有显式 AllowResourcesFallback 才降级。纯 Resources 项目直接传入 ResourcesProvider。
- 热更采用 GUID 暂存、完整校验和活动指针提交；失败/提交前取消保留旧版本，提交后取消报告实际结果。活动指针或活动快照损坏时，读取会回退到指针记录的上一份成功快照。Provider 固定其版本目录，缺包不跨版本混用。
- ResourceServiceFactory 在首次创建 Bundle Provider 前自动执行一次启动缓存维护（仅 Play Mode，可用 AutoCleanupPersistentCache 关闭）；也可在任何持久化 Loader/路径被使用之前显式调用 HotUpdateMaintenance.PrepareStartup。保留当前/上一版本，资源已用、指针丢失或损坏、路径不安全时拒绝清理。运行中仍不清理已提交快照。
- 网络生产默认 HTTPS 且禁用重定向。file:// 和 loopback HTTP 仅由 RemoteDownloader.AllowFileUrls / AllowInsecureLocalhost 显式开启。CRC 用于意外损坏检测；清单提供 64 位 SHA-256 时会额外校验摘要，但本机校验仍不构成来源认证。
- IAppService.Initialize 保持同步；异步启动放在 Flow。同步 UI Close 保持立即关闭；CloseAsync 等待可选淡出。OpenAsync 仍表示页面激活完成。
- Save 不替代 Settings 或基础 Persistence；不会自动恢复、创建默认存档或写回迁移结果。通过业务明确调用恢复/删除/保存。
- PooledInstance 是值类型代次句柄，直接 Dispose 的预热取还路径不分配；避免装箱。场景统一持有池本身。

## 卸载

先移除导入的示例、业务引用及相应 testables，再移除包；保留其他包仍需要的传递依赖。卸载可选模块不会使基础包出现程序集引用缺失。
UPM 卸载不会删除 persistentDataPath 中的设置或缓存，用户数据清理由应用明确决定。

## 本机工具

```powershell
./tools/Prepare-ConsumerProject.ps1 -Destination E:/FreshFoundation -PackageSet Full
./tools/Prepare-ConsumerProject.ps1 -Destination E:/FreshOptional -PackageSet Full -IncludeInput -IncludeSave
./tools/Prepare-ConsumerProject.ps1 -Destination E:/FreshMinimal -PackageSet Minimal
```

目标目录必须不存在。Full 复制新样例、旧样例兼容脚本、Editor 检查和消费侧测试；同时开启两个可选开关时加入可选样例测试。
手动执行 Unity 的 Yato.ConsumerVerification.Verify 检查场景与真实配置资产，再运行 PlayMode 测试。BuildWindows 构建基础 Player；BuildFocusWindows 构建可视焦点示例。
工具只修改创建的消费工程设置，可选示例工程设置 Both 输入后应重启一次 Unity 再执行交互测试。无 Actions 或其他 CI 配置。


0.6 新增接口与行为见 [0.6 指南](../Packages/com.yato.framework/Documentation~/foundation06.md)。
