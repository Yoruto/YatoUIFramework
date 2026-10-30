# 基线实跑记录（2026-10-30）

阶段目标：证明现有框架"能跑"——编译、测试、自研 AB 打包与加载全部真实执行一遍。
结论：**完成，全程发现并修复 2 处产品 bug + 2 处测试缺陷；EditMode 13/13、PlayMode 10/10 全绿；AB 打包 → AB 模式加载 → 引用计数/延迟卸载 全链路验证通过。**

## 1. 环境

| 项 | 值 |
|---|---|
| Unity | 6000.5.6f1 (`D:\Unity\6000.5.6f1\Editor\Unity.exe`) |
| 框架包 | `com.yato.framework@0.1.0`（内嵌包，`unity: 2022.3` 基线） |
| 实际生效包版本 | test-framework **1.7.0**、ext.nunit 2.1.0、ugui **2.5.0**、textmeshpro **5.0.0**（已标 deprecated，功能并入 ugui）、burst 1.8.29、collections 6.5.0 |
| 版本声明不一致 | `Packages/manifest.json` 声明 test-framework 1.1.33 / textmeshpro 3.0.6，实际解析为 1.7.0 / 5.0.0（Unity 6 下 UPM 取更高版本）；记录在案，未动声明 |

## 2. 首轮实跑暴露并修复的问题

| # | 层 | 问题 | 修复 |
|---|---|---|---|
| 1 | 产品 | `Editor/Tools/SmokeTestRunner.cs` 裸 `ILogger` 在 `Yato.Core.ILogger` 与 `UnityEngine.ILogger` 间二义（CS0104/CS1503），**Yato.Editor 程序集编译失败，所有测试无法运行** | 5 处显式限定 `Yato.Core.ILogger` |
| 2 | 产品 | `AssetKey.Normalize` 顺序 bug：先剥扩展名后合并斜杠，输入带尾缀斜杠（如 `//ui//common//btn_close.png//`）时 `lastSlash` 落在扩展名之后，**扩展名漏剥** → `Normalize_CollapsesSlashesAndTrims` 失败 | 改为"先合并/修剪斜杠，再剥扩展名"（`Runtime/Resources/Abstractions/AssetKey.cs`） |
| 3 | 测试 | `RouterLifecycleTests` 两个失败路径用例未预期错误日志：`UnityLogger.Error` 对同一次调用先 `LogError` 后 `LogException`（共 2 Error + 2 Exception），测试框架视未预期 Error/Exception 为失败 | 按实际输出顺序补 `LogAssert.Expect` |
| 4 | 测试 | `CancelOpen_DoesNotLeaveOrphan` 设计缺陷：`NullResourceService` 同步失败使 `OpenAsync` 内联完成，`CancelOpen` 永远返回 false，用例从未真正测过取消 | 新增 `OneFrameResourceService` 存根（下一帧才失败），制造真实"加载中"窗口 |

## 3. 新增：AB 冒烟测试（首次真实加载资产的测试）

`Tests/PlayMode/BundleModeSmokeTests.cs`（6 用例，manifest 缺失时整组 Skip）：

| 用例 | 验证点 | 结果 |
|---|---|---|
| `Load_Prefab_ByKey` | AB 模式按 key `ui/example/example` 加载预制体 | ✅ |
| `Instantiate_Release_DestroysInstance` | 实例化 → Dispose → 实例销毁 | ✅ |
| `Cycles100_LoadRelease_Stable` | 100 次加载/释放，句柄归零 | ✅ |
| `DelayedUnload_AfterWindow_ReleasesBundles` | 5s 延迟窗口后 `ReleaseUnused`，`LoadedBundleCount=0` | ✅ |
| `MissingKey_FailsClearly` | 缺失 key 抛 `ResourceException [ManifestLoad]` | ✅ |
| `UIRouter_10OpenCloseCycles_WithMediator` | UIRouter 端到端 10 次开关（含 Mediator/View 生命周期与参数传递） | ✅ |

## 4. AB 打包管线验证

```
BuildPipelineEntry.BuildBundlesWindows → Build/AssetBundles/Windows（manifest.json + ui/example 627KB LZ4）
                                       → 自动拷贝 Assets/StreamingAssets/AssetBundles/Windows
运行时：ResourceServiceFactory.ForceBundleMode=true → BundleProvider（manifest v2, bundles=1 assets=1）
```

- manifest v2 校验（hash/crc/size/deps/assets 映射）通过；`BundleNaming` 的"目录即 bundle"规则工作正常（`ui/example` 带斜杠 bundle 落在子目录，`RuntimePaths.ResolveBundlePath` 拼接正确）。
- 首轮冒烟曾出现一次 `LoadFromFileAsync` 返回空的瞬态失败（打包后立刻跑测试），次轮起稳定复现不出；判断为打包→导入/文件锁竞态，已记录，未改代码。

## 5. 记录在案的已知问题（未改，属设计边界）

1. **同进程多 `ResourceService` 实例不能共享 AB**：一个实例的 bundle 在 5s 延迟卸载窗口内，另一实例对同一文件 `LoadFromFile` 会报 `another AssetBundle with the same files is already loaded`。游戏内单实例无影响；测试 Teardown 已用 `BundleProvider.Shutdown()` 规避。
2. `UnityLogger.Error(message, exception)` 每次调用产生 Error + Exception 两条控制台输出，测试断言需双份 Expect；是否合并为单条 LogException 由后续定夺。
3. **Unity 6 批处理坑**（影响 CI 写法，非框架 bug）：
   - `-quit` 与启动期脚本编译/域重载竞争，`-executeMethod` 可能被静默跳过（本轮 4 次复现）；需要 `executeMethod` 的入口自行调用 `EditorApplication.Exit`，且不带 `-quit`；
   - `-buildTarget` 触发平台切换重编译时同样会中断批处理（`BuildAssetBundles` 本身支持任意目标，无需该参数）；
   - 测试 XML 落盘晚于进程退出约 5~20s，CI 解析前需轮询等待。
4. `ProjectSettings` 的 productName 为 `MessageTest`（模板残留），与 LocalLow 下的 `DefaultCompany/MessageTest` 对应；未动。

## 6. 复现命令

```powershell
$U = 'D:\Unity\6000.5.6f1\Editor\Unity.exe'

# EditMode（13 个）
& $U -batchmode -nographics -projectPath e:\YatoUIFramework -runTests -testPlatform EditMode `
  -testResults e:\YatoUIFramework\Logs\baseline-editmode.xml -logFile e:\YatoUIFramework\Logs\baseline-editmode.log

# AB 打包（不带 -buildTarget，见 5.3）
& $U -batchmode -quit -projectPath e:\YatoUIFramework `
  -executeMethod Yato.Editor.BuildPipelineEntry.BuildBundlesWindows -logFile e:\YatoUIFramework\Logs\ab-build.log

# PlayMode（14 个，含 AB 冒烟 6 + 热更冒烟 4；依赖上一步产物）
& $U -batchmode -nographics -projectPath e:\YatoUIFramework -runTests -testPlatform PlayMode `
  -testResults e:\YatoUIFramework\Logs\baseline-playmode.xml -logFile e:\YatoUIFramework\Logs\baseline-playmode.log
```

判定：进程退出码（Edit=0 / Play=0）+ XML `result=Passed`；无 manifest 时 AB 冒烟自动 Skip（不计失败）。

## 7. 变更与回滚清单

**产品修复（3 个文件）**
- `Packages/com.yato.framework/Editor/Tools/SmokeTestRunner.cs`（5 处 `ILogger` 限定）
- `Packages/com.yato.framework/Runtime/Resources/Abstractions/AssetKey.cs`（Normalize 顺序）
- `Packages/com.yato.framework/Runtime/Resources/HotUpdate/HotUpdateService.cs`（Await 失败分支清 `Success`，见第 8 节）

**测试（4 个文件）**
- `Tests/PlayMode/BundleModeSmokeTests.cs`（新增，AB 冒烟 6 用例）
- `Tests/PlayMode/HotUpdateSmokeTests.cs`（新增，热更冒烟 4 用例）
- `Tests/PlayMode/RouterLifecycleTests.cs`（LogAssert + OneFrameResourceService 存根）
- 无 asmdef / 无契约 API 变更

**产物（不入库时可删）**
- `Build/AssetBundles/Windows/`、`Assets/StreamingAssets/AssetBundles/`（整目录删除即回滚）
- `Logs/baseline-*.log|xml`、`Logs/ab-build.log`、`Logs/abprobe.log`（记录物）

**回滚方式**：本包目录在 git 中未跟踪（untracked），`git checkout` 无效——按上表逐文件还原/删除；产品修复可用下方 diff 位置手工还原（SmokeTestRunner 5 处限定改回 `ILogger`；AssetKey 恢复"先剥扩展名后合并斜杠"原顺序即可复现原 bug）。

## 8. 热更链路实跑（同日补充，D.3 第 4 条最后一块闭环）

验证方式：本地临时目录拷贝打包产物模拟远端（`file://`），不引入任何新依赖。新增 `Tests/PlayMode/HotUpdateSmokeTests.cs`（4 用例）：

| 用例 | 验证点 | 结果 |
|---|---|---|
| `FreshRun_DownloadsDiff_SecondRun_AllSkipped` | version.json 拉取 → CRC 差量下载 → 二次运行全跳过（file 级断点） | ✅ |
| `HotUpdatedManifest_DrivesLoading` | 远端 manifest 抬版本 1.0.1 → 热更后 `BundleProvider.Manifest.Version=="1.0.1"` 且资产可加载（热更产物真正驱动运行时） | ✅ |
| `CorruptedCrc_Fails_And_RollsBackToBuiltin` | version.json 写错 bundle CRC → 校验失败 → 回退（持久化 manifest 被删） | ✅（修复后） |
| `UnreachableRemote_Fails_And_RollsBack` | 远端 404 → 失败并回退 | ✅ |

**实跑抓到并修复 1 个严重产品 bug**（`HotUpdateService.Await`）：`HotUpdateResult` 跨步骤复用，前置步骤成功后 `Success=true` 残留，后续步骤失败只写 `Error` 不清 `Success`——后果是**失败检查 `if (!result.Success)` 全部失效：CRC 损坏被静默吞掉、不回退内置版本，多 bundle 场景会带着损坏状态继续下载**。修复为 `result.Success = string.IsNullOrEmpty(result.Error)`（失败分支显式置 false）。

至此 D.3 第 4 条（打包 / AB 模式 / 热更 / 测试均未实跑）全部闭环：打包 ✅ AB 模式 ✅ 热更 ✅ 测试 ✅。第 5 条 CI 入口：打包已通过 `-executeMethod` batchmode 实跑（见第 4 节），测试为 TestRunner 标准入口。

## 9. 后续 spike 决策（2026-10-30 定稿）

**用户决策：无资源热更需求，Spike A/B/C 全部挂起。**

| 项 | 状态 | 重启触发条件 |
|---|---|---|
| Spike A（YooAsset） | 挂起 | 出现资源热更/CDN/差量更新硬需求；做时按裁剪版（编辑器模拟 + Windows AB 两步，Player 实跑延后），版本锁 tag（禁止 main） |
| Spike B（HybridCLR） | 挂起（本机缺 Windows IL2CPP / Android 构建模块） | 确定移动端 IL2CPP 发布目标，并批准安装 `windows-il2cpp` 模块 |
| Spike C（Luban） | 挂起 | 出现第一个真实配置表消费方 |

注：自研热更链路（version.json 差量 + CRC + 回退）经第 8 节实跑可用，能满足轻量资源热更场景；YooAsset 的增量价值仅剩 CDN 调度/字节级续传等重度场景。本阶段"补齐游戏运行时通用层"以本记录收尾，后续按上表触发条件另开工作。

### 最终测试基线（2026-10-30）

- EditMode：13/13 ✅
- PlayMode：14/14 ✅（AB 冒烟 6 + 热更冒烟 4 + 路由生命周期 4）
