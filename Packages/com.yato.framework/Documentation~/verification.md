当前 0.5 结果见 [框架实施记录](foundation-verification.md)。以下保留为 0.3 的历史验证记录。

# 0.3.0 优化实施与验证记录（2026-10-31）

本轮按《稳定性与复用能力改进计划》实施；没有新增 CI、GitHub Actions、远程构建或发布标签。以下 PASS 来自本机实际执行，未执行项明确保留。

## 交付状态

| 计划项 | 本轮结果 | 剩余限制 / 发布门槛 |
|---|---|---|
| IMP-01 | BundleRelativePath、链接/根目录校验、重复/保留/碰撞名称、数字/平台/数量/总量校验、HTTPS 默认策略、有界下载与流式 CRC | 未做真实生产 HTTPS 服务的证书/重定向端到端验证；CRC 不等于来源认证 |
| IMP-02 | GUID 暂存、完整快照校验、D04 原子活动指针、Busy、取消与最后成功版本保留、旧缓存只读兼容、Provider 固定目录 | 已提交版本保守保留；自动旧版本垃圾回收延后，长期磁盘容量需管理 |
| IMP-03 | 完整/精简嵌入式安装指南、传递依赖表、版本同步、Unity 6 空工程安装与精简包移除验证 | 仅支持 Unity 6；完整/精简安装已在 6000.5.6f1 验证；未实施单 Git URL 解析或 Registry 发布 |
| IMP-04 | BasicSample / FullSample 自带场景、Resources 预制体、操作界面；无 GameRes / TMP 字体依赖 | Package Manager 导入按钮本身未做图形界面手点；同一 Samples~ 内容已复制到全新工程实测 |
| IMP-05 | 旧 AppBootstrapSample 和新 FullSample 成对退订、幂等启停、失败清理；正式 Bundle 默认严格报错，显式授权才降级 | Domain Reload 关闭的多次人工 Play 会话仍需按清单验收 |
| IMP-06 | 可选 Input：运行时 asset 副本、上下文、订阅清理、交互重绑定完成/取消、JSON 保存/恢复默认 | 本机 Unity 6 实际解析 Input System 1.20.0；声明最低 1.7.0 尚未单独实测 |
| IMP-07 | BasicSample 和消费侧测试实际运行无 Facade 页面，保留现有包结构；已记录源码边界与成本 | Configure 签名仍含 IFacade，调用方 asmdef 仍需 Yato.Mvc；不强制拆包 |
| IMP-08 | 本机全量 EditMode/PlayMode、消费侧集成与 Windows Player 冒烟通过 | 交互画面/声音与第二个真实游戏集成仍未完成，不能视为正式发布验收完结 |

版本基线：仅支持 Unity 6，所有 Yato 包的 `unity` 字段为 `6000.0`；当前开发与实测版本为 `6000.5.6f1`。其他 Unity 6 小版本不据此宣称已经实测。支持范围调整后，已用 `6000.5.6f1` 重新完成包解析与编译检查并正常退出，日志为 `Logs/Unity6-Baseline-Validation.log`；本次未改动运行时代码，未重复执行下表的全量测试。

版本：Framework 0.3.0、Resources 0.2.0、Input 0.1.0；其他子包维持 0.1.0。包间声明版本已核对一致，运行时代码未引入 UnityEditor 引用。

## 实际执行结果

环境：Windows、Unity 6000.5.6f1。下表仅记录修正后最终一轮结果；开发中发现并修正了 Input 的编辑器销毁、无焦点输入测试配置和新样例程序集/Settings 调用问题。

| 执行项 | PASS | FAIL | SKIP | 证据 |
|---|---:|---:|---:|---|
| 源工程 EditMode 全量（含 Input） | 113 | 0 | 0 | Logs/Optimization-EditMode.xml |
| 源工程 PlayMode 全量（含 Input / 真实 AB） | 68 | 0 | 0 | Logs/Optimization-PlayMode.xml |
| 独立消费工程 PlayMode 集成 | 2 | 0 | 0 | Logs/Consumer-PlayMode.xml |
| 完整嵌入式安装与样例场景/预制体检查（不含 Input） | PASS | — | — | Logs/Consumer-Full.log |
| 精简五包安装（不含 Input / Editor） | PASS | — | — | Logs/Consumer-Minimal.log |
| 精简包移出 Packages 后重新编译 | PASS | — | — | Logs/Consumer-Minimal-Uninstall.log |
| Windows Development Player 构建 | PASS | — | — | Logs/Consumer-WindowsBuild.log |
| Windows Player 无图形冒烟 | PASS | — | — | Logs/Consumer-WindowsPlayer.log |

合计 **183 个测试用例通过，0 失败，0 跳过**。真实 AB 产物已存在，BundleModeSmokeTests / HotUpdateSmokeTests 实际执行，未把 Skip 算成通过。新增事务测试使用真实 file:// 下载，但其合成内容不冒充可加载 AB；真实 AB 加载由独立冒烟组及 Player 再验证。

本机证据保存在源工作区 Logs（git 忽略目录）；消费工程位于 E:/YatoConsumerVerifyFull 和 E:/YatoConsumerVerifyMinimal，未复制源工程 Assets 或 ProjectSettings。精简卸载验证采用把包移到工程根目录 RemovedPackages 的可逆方式，Unity 不再发现这些包。

## 已覆盖的关键行为

- 路径遍历、Windows 设备名、分隔符歧义、保留名称、大小写重复和文件/目录碰撞均拒绝。Windows junction 在子路径与根目录两处均被拒绝，外部目录无文件产生。
- 非法平台、CRC 范围、负数/分数/大小/数量/总量超限失败；不允许非预期 URL，file:// 仅显式测试授权。
- 下载超限/长度不符不替换目标，临时文件清理；首次提交、重复跳过、版本切换后旧文件仍可读取。
- CRC/manifest 失败、缺失依赖图、取消、Busy、残留暂存恢复、首次指针提交失败、锁住已有指针造成提交失败均有回归。
- 提交前取消保留旧版本；提交后的进度回调取消仍报告成功。
- BundleRequired 给出 manifest 路径、平台、Provider、阶段与原始异常；AllowResourcesFallback 才返回 ResourcesProvider。
- Input 上下文、asset 不污染源文件、订阅退订、重绑定完成/取消、状态恢复、绑定 JSON 和重复 Dispose 通过。
- 消费侧打开无 Facade UI、Single 场景切换释放旧页面；旧 DemoAppService 与新 FullSampleService 各多次 Initialize/Shutdown，订阅数量恢复基线。
- Windows Player 使用显式 --yato-smoke-root 参数完成 Bootstrap → UI → Single/Additive → Settings Apply → Audio → 热更下载/提交 → 实际 AB 加载/释放 → Shutdown，并正常退出；业务流程日志无异常。该次为 Mono Development、-batchmode -nographics，不代表人工图形/声音或 IL2CPP 验收。

## 重复执行

本机准备工具：tools/Prepare-ConsumerProject.ps1。Full 模式复制两个新样例和旧样例脚本用于兼容性回归，后者不自动注册服务。

示例命令：
```powershell
./tools/Prepare-ConsumerProject.ps1 -Destination E:/FreshYatoConsumer -PackageSet Full
# 使用目标 Unity 安装的 Unity.exe：
Unity.exe -batchmode -nographics -projectPath E:/FreshYatoConsumer -executeMethod Yato.ConsumerVerification.Verify -quit -logFile <log>
Unity.exe -batchmode -nographics -projectPath E:/FreshYatoConsumer -runTests -testPlatform PlayMode -testFilter Yato.ConsumerTests -testResults <xml> -logFile <log>
Unity.exe -batchmode -nographics -projectPath E:/FreshYatoConsumer -executeMethod Yato.ConsumerVerification.BuildWindows -quit -logFile <log>
E:/FreshYatoConsumer/Build/YatoConsumer.exe -batchmode -nographics --yato-smoke-root <已打包的AB根目录> -logFile <log>
```

Windows Player 的 AB 冒烟 root 应包含 Windows/version.json、manifest.json 和 ui/example 包。输入/取消/故障注入的详细人工步骤继续使用既有手动验证清单。

## 未完成且不能标记通过

1. Input System 最低声明版本 1.7.0 的实测。
2. 图形界面下 Package Manager Import、完整画面、声音、交互与关闭 Domain Reload 多次 Play 的人工核验。
3. 真实生产 HTTPS 来源及证书/网络行为验收；IL2CPP 与其他平台 Player。
4. 在另一个真实游戏中验证复用成本和长期版本缓存容量管理；自动旧快照清理。
5. 正式发布标签、Scoped Registry 或单 Git 包入口均未实施。
