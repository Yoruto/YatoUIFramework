# 嵌入式安装、示例与升级（0.7.0）

仅支持 Unity 6，包最低声明为 6000.0，当前开发与实测为 6000.5.6f1。当前结果见 [实施记录](FRAMEWORK_06_IMPLEMENTATION_2026-11-01.md)，API 见 [0.6 指南](../Packages/com.yato.framework/Documentation~/foundation06.md)。

## 包集

需要直接交付给其他工程时，使用 [UPM 模块包与离线 ZIP 指南](UPM_DISTRIBUTION.md)。`tools/Export-UPMPackages.ps1` 导出 18 个独立 TGZ 和离线套件；套件的 `Install-YatoPackages.ps1` 按包集计算依赖并安装，不复制原工程游戏内容。

复制所需 com.yato.* **整个目录，含 .meta 和 link.xml** 到消费工程 Packages。嵌入式包不需要在 manifest 重复登记同名依赖，不复制原工程 Assets、Library 或 ProjectSettings。

| 包 | 版本 | 直接 Yato 依赖 |
|---|---|---|
| Core | 0.2.0 | 无 |
| Persistence | 0.1.1 | Core |
| Resources | 0.4.0 | Core |
| Resources.Bundle（可选） | 0.1.0 | Core、Resources |
| Resources.HotUpdate（可选） | 0.1.0 | Core、Resources、Resources.Bundle、Persistence |
| Mvc | 0.1.0 | 无 |
| UI | 0.3.0 | Core、Resources、Mvc |
| App | 0.3.0 | Core、Resources |
| Settings | 0.1.1 | Core、Persistence |
| Audio | 0.1.1 | Core、Resources |
| Flow | 0.2.0 | Core、App、Resources |
| Config | 0.2.0 | Core、Resources |
| Pooling | 0.2.0 | Core、Resources |
| Editor | 0.4.0 | Core、Resources、UI、Config、App |
| Framework | 0.7.0 | 上述完整默认包（含 Bundle、HotUpdate） |
| Input（可选） | 0.1.0 | 无；依赖 Unity Input System |
| Save（可选） | 0.2.0 | Core、Persistence |
| UI.Input（可选） | 0.2.0 | Core、UI、Input |

- 完整聚合包集共 15 包，不含 Save/Input/UI.Input；按需项目直接选择子包，不安装聚合包。
- 仅资源：Core、Resources，共两包；精简 UI：Core、Resources、Mvc、UI，共四包；BasicSample 再增加 App。
- FullSample 使用离线完整包集（OfflineFull），不依赖 Bundle 或 HotUpdate；资源热更新演示单独位于 HotUpdate 包的 HotUpdateSample。
- Save 的 CounterSample 和 UI.Input 的 FocusSample 在各自包中导入。
- 使用 Unity 6 UGUI。Editor 的旧 TMP 包声明可能出现弃用提示，TMP 功能由 UGUI 提供；基础包和新示例不引用 TMP 字体。
- 聚合包的 Git 子目录 URL 不会自动解析同仓库兄弟包。当前正式支持嵌入式安装，未发布 Registry。

## 0.6 → 0.7 资源拆包迁移

- 纯 Resources 游戏只装 Core + Resources；基础 UI 再加 Mvc + UI。不要安装全量 Framework 聚合包，否则会按声明带入 Bundle 和 HotUpdate。
- 本地 AssetBundle 游戏增加 `com.yato.resources.bundle`，把原 Bundle 工厂 API 改为 `BundleResourceServiceFactory`；使用代码的 asmdef 增加 `Yato.Resources.Bundle`。类型命名空间仍是 `Yato.Resources`。
- 需要资源更新时再增加 `com.yato.resources.hotupdate` 及 Persistence，相关 asmdef 增加 `Yato.Resources.HotUpdate`。Play/Player 默认接入快照策略；Editor 工具中可显式调用 `HotUpdateBundleCachePolicy.Install()`。
- `ForceBundleMode` 移除。Editor 直接读 AssetDatabase 用基础工厂；显式 Bundle 工厂在 Editor 也读取 Bundle。
- Bundle 构建工具移到 Bundle 包 Editor 程序集，热更窗口移到 HotUpdate 包；通用 Editor 工具不需要这两个扩展。自定义编辑器程序集按需引用 `Yato.Resources.Editor`、`Yato.Resources.Bundle.Editor` 或 `Yato.Resources.HotUpdate.Editor`。
- FullSample 改为离线演示。若之前从 Package Manager 导入了旧 FullSample，请同步更新导入副本；热更界面从 HotUpdateSample 单独导入。
- 卸载可选包前先移除业务、示例与测试对它的直接引用。旧资源代码文件及 `.meta` 已整体迁移，GUID 保持；不要并排保留旧 Runtime/HotUpdate 或旧 Bundle Provider。

## Quick Start

1. 新建 Unity 6 工程，建议 6000.5.6f1；复制需要的包集，等待编译。
2. Package Manager 导入 BasicSample，打开 Scenes/YatoBasic.unity，把它和 YatoBasicSecond.unity 加入构建场景。
3. Play，打开/关闭页面，切换 Single 场景，观察资源释放日志。
4. 导入 FullSample，打开 YatoFull.unity。ready 流程自动加载配置目录并预热中性实例池，可取还实例、停止/重启流程、暂停，以及演示设置和音频。
5. 可选导入 CounterSample，打开 CounterSample.unity；递增、保存两次、加载或显式恢复备份。
6. 可选导入 FocusSample，打开 FocusSample.unity，将 Active Input Handling 设为 Input System 或 Both；用鼠标、方向键/Enter 或手柄操作弹窗和返回。
7. 各示例单独运行；没有自动注册的未运行示例，也没有 Yato Survivor 内容。

## 升级与行为

- 各模块版本独立维护，整体功能版本为 Framework 0.7.0；替换完整包目录，保留 GUID，避免并排安装多个版本。
- 导入到 Assets 的样例是副本，升级不会自动覆盖。备份或移除旧副本后再导入，避免重复 GUID。
- 基础 ResourceServiceFactory.Create 默认 EditorAssetProvider / Player ResourcesProvider；Bundle 项目显式使用 BundleResourceServiceFactory.Create / CreateAsync，默认 BundleRequired，只有显式 AllowResourcesFallback 才降级。
- 热更采用 GUID 暂存、完整校验和活动指针提交；失败/提交前取消保留旧版本，提交后取消报告实际结果。活动指针或活动快照损坏时，读取会回退到指针记录的上一份成功快照。Provider 固定其版本目录，缺包不跨版本混用。
- BundleResourceServiceFactory 在首次创建 Bundle Provider 前调用当前缓存策略的启动维护（仅 Play Mode，可用 AutoCleanupPersistentCache 关闭；未安装热更时为无操作）；也可在任何持久化 Loader/路径被使用之前显式调用 HotUpdateMaintenance.PrepareStartup。保留当前/上一版本，资源已用、指针丢失或损坏、路径不安全时拒绝清理。运行中仍不清理已提交快照。
- 网络生产默认 HTTPS 且禁用重定向。file:// 和 loopback HTTP 仅由 RemoteDownloader.AllowFileUrls / AllowInsecureLocalhost 显式开启。CRC 用于意外损坏检测；清单提供 64 位 SHA-256 时会额外校验摘要，但本机校验仍不构成来源认证。
- IAppService.Initialize 保持同步；异步启动放在 Flow。同步 UI Close 保持立即关闭；CloseAsync 等待可选淡出。OpenAsync 仍表示页面激活完成。
- Save 不替代 Settings 或基础 Persistence；不会自动恢复、创建默认存档或写回迁移结果。通过业务明确调用恢复/删除/保存。
- PooledInstance 是值类型代次句柄，直接 Dispose 的预热取还路径不分配；避免装箱。场景统一持有池本身。

## 卸载

先移除导入的示例、业务引用及相应 testables，再移除包；保留其他包仍需要的传递依赖。卸载可选模块不会使基础包出现程序集引用缺失。
UPM 卸载不会删除 persistentDataPath 中的设置或缓存，用户数据清理由应用明确决定。

## 本机工具

向尚未安装 Yato 的已有工程安装包时，使用 `./tools/Install-YatoPackages.ps1 -ProjectPath E:/MyGame -PackageSet ResourcesOnly`；只读预览加 `-WhatIf`。目标已有任意 Yato 包或 Yato manifest 注册项会被拒绝，避免混装或覆盖已有集成。下面的 `Prepare-ConsumerProject.ps1` 专用于创建全新验证工程，包含验证脚本与测试，不是生产工程安装器。

```powershell
./tools/Prepare-ConsumerProject.ps1 -Destination E:/FreshFoundation -PackageSet Full
./tools/Prepare-ConsumerProject.ps1 -Destination E:/FreshOptional -PackageSet Full -IncludeInput -IncludeSave
./tools/Prepare-ConsumerProject.ps1 -Destination E:/FreshMinimal -PackageSet Minimal
```

目标目录必须不存在。Full 复制新样例、旧样例兼容脚本、Editor 检查和消费侧测试；同时开启两个可选开关时加入可选样例测试。
手动执行 Unity 的 Yato.ConsumerVerification.Verify 检查场景与真实配置资产，再运行 PlayMode 测试。BuildWindows 构建基础 Player；BuildFocusWindows 构建可视焦点示例。
工具只修改创建的消费工程设置，可选示例工程设置 Both 输入后应重启一次 Unity 再执行交互测试。无 Actions 或其他 CI 配置。


0.6 新增接口与行为见 [0.6 指南](../Packages/com.yato.framework/Documentation~/foundation06.md)。
