# 嵌入式安装、示例与升级

适用：聚合包 `com.yato.framework` 0.3.0；仅支持 Unity 6，包的最低版本字段为 `6000.0`；当前开发与实测版本为 `6000.5.6f1`。实际已运行的版本见 [本轮验证记录](OPTIMIZATION_VERIFICATION_2026-10-31.md)。

## 安装包集

将所需包的**整个目录（含 .meta 和 link.xml）**复制到消费工程的 `Packages/`。嵌入式包无需在 manifest 中再登记同名版本依赖。不要复制原工程的 Assets、Library 或 ProjectSettings。

| 包 | Yato 依赖 | Unity 依赖 / 用途 |
|---|---|---|
| Core 0.1.0 | 无 | 基础运行时 |
| Persistence 0.1.0 | Core | D04 安全写入 |
| Resources 0.2.0 | Core、Persistence | AB / JSON / UnityWebRequest 模块；Persistence 用于活动指针原子提交 |
| Mvc 0.1.0 | 无 | 当前 UI 程序集必需，页面可不使用 MVC |
| UI 0.1.0 | Core、Resources、Mvc | UGUI |
| App 0.1.0 | Core、Resources | Bootstrap、场景和 ResourceScope |
| Settings 0.1.0 | Core、Persistence | 强类型设置 |
| Audio 0.1.0 | Core、Resources | Unity Audio 模块 |
| Editor 0.1.0 | Core、Resources、UI | UGUI、TMP；仅 Editor 平台 |
| Framework 0.3.0 | 上述全部包 | 聚合入口、样例、测试、文档 |
| Input 0.1.0（可选） | 无 | Unity Input System 1.7.0 起 |

- **完整包集**：复制除 Input 外的全部 `com.yato.*`；需要 Input 时另加它。聚合包不强制依赖 Input。
- **精简 UI 包集**：Core + Persistence + Resources + Mvc + UI，共五包。文档中的“Core + Resources + UI”是功能组合，仍需包含其传递依赖。无需 TMP、Settings、Audio、Editor 或 Input。
- 使用 BasicSample 时再加 App；可从聚合包源目录复制 BasicSample 到 Assets。安装完整聚合包时可用 Package Manager → Yato Framework → Samples → Import。
- 使用 Unity 6 的 UGUI；TMP 功能由 UGUI 提供，完整 Editor 工具的旧 TMP 包依赖可能出现弃用提示。基础运行时与新示例不引用 TMP。

仓库是多 UPM 包结构。**仅添加聚合包的 Git 子目录 URL 无法自动解析同仓库的兄弟包**。当前正式支持嵌入式复制；Scoped Registry 和单包重组未实施。

## 10–15 分钟 Quick Start

1. 新建 Unity 6 工程（建议使用当前实测版本 `6000.5.6f1`），按上述包集复制目录，等待编译。
2. 在 Package Manager 导入 **BasicSample**，打开 `Scenes/YatoBasic.unity`。
3. 将 `YatoBasic.unity` 和 `YatoBasicSecond.unity` 加入 Build Settings / Build Profiles 的场景列表。
4. Play，点 **Open page (no PureMVC)**。页面从样例自己的 Resources 加载，Console 打印资源持有日志。
5. 点页面内关闭按钮，观察释放日志；再次打开后点 **Switch scene (Single)**，旧页面关闭，资源释放。
6. 停止运行；可单独导入 **FullSample**，打开 `Scenes/YatoFull.unity`。播放生成音效、调整音量并点击 Save，重启核对配置。其音频由代码生成，不需要下载素材。
7. FullSample 的热更演示需要已发布的真实 AB 文件。默认 HTTPS；本地测试必须勾选允许 file:// / loopback HTTP。成功更新后重新创建资源 Provider 才会使用新版本；已创建 Provider 继续使用旧版本。
8. 两个新示例可同时导入；只打开当前要运行的场景。它们没有全局自动服务注册钩子，也不与未运行的示例竞争路由配置。

示例通过运行时 OnGUI 提供操作控件，不依赖 TMP 字体、原工程 EventSystem 或旧输入后端。旧 `YatoFrameworkDemo` 保留供旧项目迁移，不再作为 Package Manager 的推荐导入项。

## 0.2 → 0.3 行为迁移

### 明确资源策略

Bundle 模式的 `CreateProvider()` / `Create()` 默认严格失败。编辑器默认 AssetDatabase 的选择不变。

```csharp
// 正式 Bundle 模式：保留异常供游戏决定修复/退出策略。
var resources = ResourceServiceFactory.Create();

// 明确允许降级；不会在正式模式自动启用。
var fallback = ResourceServiceFactory.Create(
    ResourceServiceFactory.CreateProvider(ResourceFailurePolicy.AllowResourcesFallback));

// 已知使用 Resources 的样例/项目直接选择 Provider。
var preview = ResourceServiceFactory.Create(new ResourcesProvider());
```

错误包含 Provider、平台、manifest 路径、阶段与原始异常。Android/WebGL 内置 manifest 的异步获取仍属既有平台限制，本轮不宣称已完成这些平台验证。

### 热更缓存和网络策略

新布局：`AssetBundles/{Platform}/staging/{本机GUID}/` → `versions/{本机GUID}/`，最后原子写入 `active.txt`。远端 appVersion 只做版本比对，不参与目录命名。文件名拒绝路径遍历、平台歧义、设备名和链接路径；检查与系统调用间仍存在本地文件系统竞争窗口。

旧平铺缓存在没有 active.txt 时继续按**整份清单对应目录**读取，不删除或自动改写。新更新提交后成为完整快照；Provider 固定自己的目录，缺包直接失败，不混用内置包。失败/提交前取消保留活动版本；提交后取消报告成功。多个 RunAsync 立即返回 Busy，不排队。

生产默认仅 HTTPS，禁用重定向。测试用法：

```csharp
var downloader = new RemoteDownloader {
    AllowFileUrls = true,             // 仅本地测试
    AllowInsecureLocalhost = true     // 仅 loopback HTTP
};
var result = new HotUpdateService(downloader).RunAsync(rootUrl);
```

每文件默认最大 1 GiB、清单 4 MiB、最多 10,000 包、总量 8 GiB；默认超时 60 秒、最多 3 次网络重试。长度、CRC（包括零值）均验证。CRC 检测意外损坏，**不提供来源认证**；TLS 信任来自应用提供的根 URL。未增加签名发布流程。

更新开始清理遗留的受控 staging GUID 目录。已提交快照保守保留，避免删除旧句柄仍需使用的文件；当前没有自动垃圾回收，磁盘占用会随版本增长。确认所有旧 Provider/句柄退出后，可由游戏维护流程清理不再使用的版本；先保留 active.txt 当前及上一版本。此限制不影响失败恢复，但发布前应评估长期容量。

### Input

复制 Input 包后，UPM 解析官方 Input System。按项目选择 Active Input Handling，参考 [Input 包说明](../Packages/com.yato.input/README.md)。其他包没有 Yato.Input / Unity.InputSystem 程序集引用；卸载 Input 不影响其编译。

## 更新和卸载

- 升级前停止 Play，备份所需用户配置；以完整包目录为单位替换，保持 .meta/GUID，不把两个版本并排放入 Packages。
- 聚合版本与子包独立维护；本轮 Resources 0.2.0、Input 0.1.0、Framework 0.3.0，其他子包维持原版本。
- 示例导入在 Assets 下形成副本；升级不会覆盖已修改的副本。先移除/备份旧导入，再导入新版本，避免同 GUID 多份副本。
- 卸载时先删除样例副本和业务中的 Yato 引用，再移除相关包目录；保留包的传递依赖。不要复制或替换 ProjectSettings。
- persistentDataPath 下的用户配置与已下载缓存不会随 UPM 卸载自动删除，删除数据由游戏明确决定。

## 本机手动验证工具（无 CI）

`tools/Prepare-ConsumerProject.ps1 -Destination <不存在的空工程路径> -PackageSet Minimal|Full [-IncludeInput]` 仅准备独立消费工程；不会复制原工程设置或游戏资源。Full 另外复制独立样例、旧样例兼容脚本、Editor 检查器与消费侧 SmokeTests；ConsumerPlayerSmoke 仅在传入 --yato-smoke-root 时运行。

使用目标版本 Unity 打开新工程；或手动执行：
`Unity.exe -batchmode -nographics -projectPath <目录> -executeMethod Yato.ConsumerVerification.Verify -quit -logFile <日志>`。
Windows 构建使用 `Yato.ConsumerVerification.BuildWindows`；消费侧 PlayMode 测试用 `-runTests -testPlatform PlayMode -testFilter Yato.ConsumerTests`。

这些是开发者按需执行的本机工具，没有新增 Actions、定时任务或远程构建配置。
