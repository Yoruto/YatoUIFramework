# YatoFramework 手动验证清单（2026-10-31）

> 适用版本：`com.yato.framework` 0.2.0（多包结构）
> 对应设计：`docs/YatoFramework_ARCHITECTURE_AND_ROADMAP_2026-10-30.md`（§5、§5.2）
> 自动化用例由本仓库提供，但**必须在 Unity 编辑器中手动执行并记录结果**。

## 0. 前置准备

| # | 检查项 | 预期 |
|---|---|---|
| 0.1 | 用 Unity 打开工程（本工程为 6000.5.6f1） | Console 无 asmdef / package 解析错误 |
| 0.2 | Package Manager 列表 | 能看到 `Yato Framework` 及 `Core / Mvc / Resources / UI / App / Persistence / Settings / Audio / Editor / Input(占位)` |
| 0.3 | `Packages/manifest.json` | 含 `"testables": ["com.yato.framework"]` |
| 0.4 | 各包根目录 | 均有 `package.json`；运行时包另有 `link.xml` |
| 0.5 | `com.yato.input` | 只有 `package.json` + `README.md`，无程序集 |

## 1. 自动化测试（Unity Test Runner）

### 1.1 EditMode

| 测试文件 | 关注点 | 通过标准 |
|---|---|---|
| `AssetKeyTests` | key 规范化 | 全绿 |
| `ManifestTests` / `BundleRefCounterTests` | manifest 解析、依赖顺序、环检测、延迟卸载去重 | 全绿 |
| `PageStackTests` | OnCover/OnReveal、返回键跳层 | 全绿 |
| `ResourceServiceTests` | 同 key 单飞、多持有者计数、取消回滚、类型不符回滚、RefCount 实时 | 全绿 |
| `UIPageCacheTests` | 同 id 替换返回旧实例、LRU、取出语义 | 全绿 |
| `ResourceScopeTests` | 反向释放、迟到结果立即释放、单句柄释放、异常隔离 | 全绿 |
| `FileStoreTests` | 路径安全、UTF-8 无 BOM、覆盖、缺文件、同路径忙、提交失败、临时文件清理、序列化 | 全绿 |
| `SettingsServiceTests` | 默认值模板、缺文件/损坏/非法、Apply 通知不落盘、Save 校验、退订、异步 | 全绿 |

> 若 `FileStoreTests` 的 `CommitFailure_*` 失败，检查平台是否支持 `File.Replace`（D04 要求失败即报错，不降级覆盖）。

### 1.2 PlayMode

| 测试文件 | 关注点 | 通过标准 |
|---|---|---|
| `RouterLifecycleTests` | 五层 Canvas、无资源服务失败、取消打开、返回键 | 全绿 |
| `RouterConcurrencyTests` | 加载中重复打开只建一个实例、缓存页句柄释放一次、非法层级无残留 | 全绿 |
| `SceneServiceTests` | Single/Additive、Busy、取消窗口、InvalidScene、后端异常、Ready 等待、初始化器失败/超时、最后场景保护、资源释放 | 全绿 |
| `GameBootstrapTests` | 顺序初始化/反序关闭、幂等、失败回滚、重复注册忽略、DDOL 根、全局资源释放 | 全绿 |
| `AudioServiceTests` | 通道音量合成、交叉渐变、暂停恢复、多实例、池满拒绝/抢占、结束回池、句柄释放、场景范围绑定、Shutdown | 全绿 |
| `BundleModeSmokeTests` | 真实 AB 加载/卸载 | 先执行 `Yato/Build/Bundles`，否则整组 Skip |
| `HotUpdateSmokeTests` | 热更下载/回滚 | 需要本地静态服务器，未准备时 Skip |

记录：EditMode 通过 __/__，PlayMode 通过 __/__，Skip __ 个（原因：__________）。

## 2. D02 初始化与生命周期

| # | 操作 | 预期 |
|---|---|---|
| 2.1 | 新建空场景直接 Play（无 `GameBootstrap` 组件） | Console 出现「编辑器自动初始化完成（未使用 Bootstrap Scene）」；`GameBootstrap.IsInitialized == true` |
| 2.2 | Play 中手动再调用 `GameBootstrap.Initialize()` | 不重复初始化、无重复服务、无第二个 DDOL 根 |
| 2.3 | Hierarchy 查看 `[Yato.App]` | 只有一份，且位于 `DontDestroyOnLoad` 场景 |
| 2.4 | 切换场景（Single） | `[Yato.App]` / `[Yato.Audio]` 仍存在，服务不被重建 |
| 2.5 | 注册一个会抛异常的服务后 Play | 已完成的服务被 `Shutdown`，`LastInitializationError` 有值，`IsInitialized == false` |
| 2.6 | Editor：Project Settings → Editor → Enter Play Mode Options 关闭 Domain Reload 后再 Play | 无 `Facade Singleton` 类报错、无重复订阅、`RuntimeHost` 事件不残留 |
| 2.7 | 退出 Play | `Shutdown` 按反序执行（可在服务里打日志确认）、无异常 |

## 3. D03 场景系统

准备：两个场景 `A`、`B` 加入 Build Settings；可选一个会延时完成的 `ISceneInitializer`。

| # | 操作 | 预期 |
|---|---|---|
| 3.1 | `LoadAsync(B)`（Single） | `Success`，`Context.State == Ready`，`ActiveContext` 指向 B |
| 3.2 | 加载过程中立刻再次 `LoadAsync` | 第二次立即拿到 `Busy`，不排队 |
| 3.3 | 调用后同帧 `cts.Cancel()` | `Canceled`，Stage = `CancelCheck`，且 Unity 未真正开始加载（无场景切换） |
| 3.4 | 加载一个不在 Build Settings 的场景 | `InvalidScene`，Stage = `Validate` |
| 3.5 | 带初始化器加载，初始化器 3 秒完成 | 前 3 秒 `IsBusy == true`、`Context.State == Loaded`；完成后 `Ready` |
| 3.6 | 初始化器抛异常 | `Failed`，Stage = `Ready`，场景**不回滚**仍在列表中 |
| 3.7 | 初始化器超时（把超时设 0.5s，任务 3s 才结束） | 超时后 `IsBusy` 仍为 true；任务真正结束后才变 false，结果为 `Failed` |
| 3.8 | Additive 加载第二个场景 | 两个 `SceneContext` 各自独立 `Resources`，`ActiveContext` 不变 |
| 3.9 | 在场景上下文里 `Track` 一个句柄后卸载 | 句柄被释放；卸载后 `Track` 立即 Dispose |
| 3.10 | 尝试卸载最后一个场景 | `Failed`，Stage = `Unload`，场景仍在 |

## 4. D04 持久化

数据根目录：`Application.persistentDataPath`（Editor 下 `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>`）。

| # | 操作 | 预期 |
|---|---|---|
| 4.1 | 正常写入 `settings/demo.json` 后查看文件 | 目录自动创建、UTF-8 无 BOM、无 `*.yato-tmp` 残留 |
| 4.2 | 覆盖写入同一文件 | 内容更新，旧文件不残留、无临时文件 |
| 4.3 | 写入 `../escape.json`、`/abs.json`、`C:\x.json`、`a:b` | 均抛 `ArgumentException`，不产生文件 |
| 4.4 | 读不存在的文件 | `FileNotFoundException`，不创建空文件 |
| 4.5 | 手工把目标文件改成只读 / 用同名目录占位后再写 | 抛 `FileCommitException`，原文件不被覆盖，临时文件被清理 |
| 4.6 | 写入时传已取消的 token | `OperationCanceledException`，原文件内容不变 |
| 4.7 | 手工放一个 `xxx.yato-tmp` 残留文件 | `EnumerateFiles` 不返回它；`CleanupTemporaryFiles()` 能清理 |
| 4.8 | 在持有 `AcquireLock` 期间对同路径读写 | 抛 `FileBusyException`；释放后恢复正常 |

## 5. D05 设置

| # | 操作 | 预期 |
|---|---|---|
| 5.1 | 首次启动（无配置文件） | `SettingsLoadResult.Status == DefaultsUsed`，使用默认值，不创建文件 |
| 5.2 | 修改后 `Save()` 再重启 | `Loaded`，值一致 |
| 5.3 | 把 JSON 清空/写坏 | `Corrupted`，文件内容保持原样，运行期用默认值 |
| 5.4 | 写入校验不通过的值 | `Invalid`，文件保持原样，运行期用默认值 |
| 5.5 | `Apply` 后不调用 `Save` 直接退出 | 磁盘文件不变（不自动落盘） |
| 5.6 | 校验不通过时调用 `Save` | 抛 `SettingsValidationException`，不写盘 |
| 5.7 | 订阅 `Changed` 后 `Apply/ResetToDefaults/Load` | 各触发一次；退订后不再触发 |
| 5.8 | `ResetToDefaults` 后再看 `Current` 与 `CreateDefaultsSnapshot` | 均回到默认值，默认模板未被运行期修改污染 |

## 6. D06 音频

| # | 操作 | 预期 |
|---|---|---|
| 6.1 | 播放 BGM，切换到另一个场景（Single） | 音乐不中断（`[Yato.Audio]` 为 DDOL） |
| 6.2 | `PlayMusic(B, 0.5s)` | 旧曲淡出停止、新曲淡入，渐变结束后 `CurrentMusicVolume` 到目标值 |
| 6.3 | 暂停 / 恢复 | `IsMusicPaused` 正确，恢复后从原位置继续 |
| 6.4 | 同一音效连续播放多次 | 多实例并存，各自独立句柄 |
| 6.5 | 池容量设 1，连续播放 2 个循环音效（默认策略） | 第二个 `TryPlaySfx` 返回 false |
| 6.6 | 池容量设 1 + `ReplaceLowestPriority`：低优先级在播，再请求高优先级 | 请求成功，旧句柄 `IsReleased == true` |
| 6.7 | 播一次性短音效后等待 | 播放结束自动回池，`ActiveSfxCount == 0`，句柄释放 |
| 6.8 | 循环音效 `Stop()` | 立即回池，`ActiveSfxCount` 减少 |
| 6.9 | 通过资源 key 播放，停止后看资源诊断窗口 | AudioClip 句柄已释放，bundle 不被提前卸载 |
| 6.10 | 循环音效 `sceneContext.Resources.Track(handle)` 后卸载场景 | 音效停止、句柄释放 |
| 6.11 | 设置音量变化 → `SetBusVolume` | 在播音效/音乐音量立即变化；配置 `AudioMixerVolumeSink` 时 Mixer 参数同步 |
| 6.12 | `Shutdown()` | 音乐/音效全部停止释放，`[Yato.Audio]` 被销毁 |

## 7. §5.2 最小集成验收流程对照

| 设计条目 | 手工步骤 | 结果 |
|---|---|---|
| 1. Windows Player 从 Bootstrap 启动且只初始化一次 | Build Settings 只放 Bootstrap Scene → Build → 运行；日志确认一次初始化 | ☐ |
| 2. 加载设置默认值，首次无文件不报致命错误 | 删除 persistentDataPath 下配置文件后运行 | ☐ |
| 3. 播放 BGM 并加载主场景 | 运行 Bootstrap → `PlayMusic` → `LoadAsync` | ☐ |
| 4. Single 切换场景：BGM 继续、原场景资源释放 | 切场景后看 Leak Report / Resource Window | ☐ |
| 5. Additive 加载并等待初始化器 Ready | 见 3.5 / 3.8 | ☐ |
| 6. 冲突请求得 Busy；失败时 Loading UI 能结束 | 见 3.2；用不存在的场景名触发失败 | ☐ |
| 7. 改音量并显式保存，重启恢复 | 见 5.2 / 6.11 | ☐ |
| 8. 异步保存同路径 Busy、取消、临时文件清理 | 见 4.5 / 4.6 / 4.8 | ☐ |
| 9. 安装 Input 模块后的上下文切换与重绑定 | **未实现**（D07 占位） | N/A |
| 10. 退出时反序清理，无重复释放与悬挂回调 | 见 2.7 / 6.12；Console 无异常 | ☐ |

## 8. 发布前回归

- [ ] 所有 EditMode / PlayMode 用例通过（记录数量）
- [ ] Windows Player 构建成功，启动无报错
- [ ] Unity 2022.3 与 Unity 6 各导入一次，无编译错误（最低版本字段为 2022.3）
- [ ] 未安装 `com.yato.input` 时可正常编译
- [ ] 新增公开 API 已在 `CHANGELOG.md` 记录
- [ ] `package.json` 版本与 CHANGELOG 一致

## 9. 记录模板

```
日期：____   Unity 版本：____   平台：Windows Player
EditMode：通过 __ / 失败 __ / 跳过 __
PlayMode：通过 __ / 失败 __ / 跳过 __
失败项（编号 + 现象 + 日志）：
1.
2.
备注：
```
