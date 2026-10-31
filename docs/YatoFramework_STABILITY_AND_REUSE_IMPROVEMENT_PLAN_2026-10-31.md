# YatoFramework 稳定性与复用能力改进计划（不含 CI）

> 文档类型：后续开发计划 / 默认实施方案  
> 制定日期：2026-10-31  
> 目标仓库：`Yoruto/YatoUIFramework`  
> 代码基线：`main`，聚合包 `com.yato.framework` 0.2.0（D01–D06 已实现，D07 占位，D08 部分完成）  
> 状态：计划已整理，**不代表问题已修复或通过 Unity 实测**。具体实施以开始开发时的最新代码为准。

## 1. 目标、范围和原则

### 1.1 目标

1. 使远程 AssetBundle 更新不越界写文件、不破坏最后一份可用版本，并能清楚报告失败。
2. 使框架能够在独立 Unity 工程中安装、运行示例、卸载与升级，而不依赖原仓库隐含资源。
3. 统一运行时服务、事件、场景、UI 与资源的所有权和退出清理行为。
4. 使资源初始化错误可见、可配置，不在正式构建中以静默降级掩盖故障。
5. 实现轻量、可选的 D07 Input 适配，并完成不依赖 CI 的回归与发布验证。

### 1.2 明确排除

- **本计划不创建 GitHub Actions、自动化流水线、CI 任务或远程构建基础设施**。可编写和补充自动化测试代码，但由开发者在 Unity Test Runner 或本机命令行手动执行并记录结果。
- 不修改已冻结 D01–D04 的核心设计决定；涉及现有公开 API 行为变化时需提供过渡方案并记录版本变更。
- 不引入大型 DI 容器、完整 ECS、自定义输入引擎、游戏业务存档、联网服务或平台 SDK。
- 不要求立即接入 Addressables、FMOD/Wwise 或发布到公共包注册表。
- 本轮只提交计划文档，不实施代码修复。

### 1.3 执行原则

- **可靠性先于新功能**：先完成 IMP-01～IMP-05，再实施 D07。
- **最小改动与清晰边界**：优先沿用 `IResourceProvider`、`ResourceScope`、`IFileStore`、`DisposableBag` 等现有机制。
- **失败不伪装成功**：只对能够保证的取消、提交、回滚和降级语义作出承诺。
- **真实项目验证**：不以仓库内可运行作为独立可复用的唯一验收标准。

## 2. 优先级与阶段总览

| ID | 优先级 | 工作项 | 主要代码/产物 | 完成判定 |
|---|---|---|---|---|
| IMP-01 | P0 | 热更新输入与路径安全 | `VersionManifest`、`HotUpdateService`、`RemoteDownloader` | 恶意/损坏清单无法写出资源根目录 |
| IMP-02 | P0 | 热更新事务与失败恢复 | 热更暂存、正式版本切换、运行时路径解析 | 失败/取消后上一成功版本仍可用 |
| IMP-03 | P0 | UPM 分发方案落地 | 子包、聚合包、安装文档 | 全新工程可按文档完成安装与卸载 |
| IMP-04 | P1 | 示例独立化与上手体验 | `Samples~`、Quick Start | 导入示例无需复制原工程 `Assets/GameRes` |
| IMP-05 | P1 | 生命周期与资源错误策略 | `AppBootstrapSample`、`ResourceServiceFactory` | 重启无重复回调；正式模式错误可定位 |
| IMP-06 | P2 | D07 Input 可选适配 | `com.yato.input` | 上下文、重绑定与清理可独立使用 |
| IMP-07 | P2 | UI/MVC 解耦可行性验证 | 小型适配层原型/结论记录 | 得到明确保留或解耦决策，不强行重构 |
| IMP-08 | P1 | 端到端人工回归与发布整理 | 测试、记录、文档、版本 | 两个 Unity 版本与 Windows Player 有可复查记录 |

推荐实施顺序：**阶段 A（IMP-01、02）→ 阶段 B（IMP-03、04）→ 阶段 C（IMP-05）→ 阶段 D（IMP-06、07）→ 阶段 E（IMP-08）**。IMP-08 中的测试用例补充应随每个阶段持续进行，最终集中验收。

## 3. 阶段 A：热更新可靠性与安全（P0）

### IMP-01：远端输入、路径和下载内容校验

现状线索：`VersionManifest.Parse` 直接保存远端 Bundle 名；`HotUpdateService` 使用 `Path.Combine(platformDir, bundleName)` 构造磁盘路径，尚无与 D04 等效的路径边界校验。`RemoteDownloader` 接收下载 URL 与落盘路径并执行文件操作。

任务：

1. 设计统一的 `BundleRelativePath` 校验入口：拒绝空名、绝对路径、盘符、`..`、`.` 路径段、反斜杠歧义、非法字符和链接逃逸；允许受控的相对目录（如 `ui/example`）。
2. 清单解析时校验 Bundle 名唯一性、名称规范、数量、非负大小、大小上限、CRC 字段范围；校验 `platform` 与当前运行平台一致。对资源根目录进行规范化并限制所有落盘目标位于其中。
3. 不直接信任远端 `appVersion`、文件名等字段作为文件系统目录名。远端 URL 的拼接应使用经验证的相对路径，并拒绝非预期协议；生产环境要求可信 HTTPS 来源（本地测试可显式允许 `file://`）。
4. 给下载添加最大文件大小/合理超时边界；下载后核对实际字节数与完整性值。CRC 继续用于意外损坏检测，但不能将其描述为来源认证；若需要对抗恶意篡改，可增加由可信发布流程提供的 SHA-256/签名校验能力。
5. 明确更新操作互斥，禁止两个 `RunAsync` 同时写相同暂存目录或活动版本状态。

验收：

- `../settings/x`、`/tmp/x`、`C:\\x`、重复/无效名称及符号链接逃逸均被拒绝，资源目录之外无文件被创建、覆盖或删除。
- 平台不匹配、长度/CRC 不符、超限文件和非法 URL 均得到明确失败结果。
- 合法多层 Bundle 路径正常下载；失败清理仅作用于本次受控的临时文件。

### IMP-02：版本化暂存、提交和回退

现状线索：`RemoteDownloader` 的目标文件处理先删除旧文件再 `File.Move`；`HotUpdateService.RollbackToBuiltin` 在失败时删除持久化 manifest。这不构成完整的“上一成功远程版本可保留”的事务语义，且资源可能在正式清单切换前被原位覆盖。

默认方案：

1. 将每次更新下载到**独立、受控的版本暂存目录**，不覆盖当前活动 Bundle 和 manifest。
2. 下载全部文件后校验每个 Bundle、`version.json` 与 `manifest.json` 的一致性，确认目标版本能够由 `ResourceManifest` 正常解析、依赖关系有效。
3. 使用小型的活动版本指针/元数据进行最终提交，复用 D04 安全写入语义；只有全部验证通过才切换活动版本。根据当前 `RuntimePaths`、`BundleLoader` 的平铺路径实现补充版本目录解析，避免新旧版本混用。
4. 失败或提交前取消：删除本次暂存，**保持上一成功远程版本不变**；没有成功远程版本时才使用 StreamingAssets 内置版本。提交阶段不能把已成功提交报告为取消。
5. 旧版本清理必须晚于新版本提交，并避开仍被资源句柄/BundleProvider 使用的文件；允许保留一份上一成功版本作为恢复点。启动时可识别并安全清理未提交的暂存目录。
6. 已公开的旧平铺目录布局要有一次性兼容识别或迁移路径；不要在未验证迁移的情况下删除旧资源。

验收：

- 首次更新成功、重复更新跳过已验证内容、成功切换版本均通过。
- 下载中断、CRC 不符、manifest 解析失败、提交失败、取消及模拟遗留暂存文件后，重新启动仍可使用最后一份成功版本。
- 切换时不会混用旧 manifest 与新 Bundle；旧句柄释放前不清除其依赖文件。
- 将既有 `HotUpdateSmokeTests` 扩展为上述失败与恢复场景，并在有真实 AB 产物时执行，不把 Skip 当作通过。

## 4. 阶段 B：UPM 可分发与独立示例（P0/P1）

### IMP-03：先完成可复现的嵌入式安装，再决定注册表发布

当前 `com.yato.framework/package.json` 通过版本号依赖同仓库的其他 `com.yato.*` 子包。Unity 的 Git 子目录安装不会自动将兄弟目录变为可解析的 Git 包依赖，因此**不能把一个聚合包 Git URL 宣称为可独立一键安装**。

默认实施：

1. 首版正式支持“复制所需 `com.yato.*` 目录到消费工程 `Packages/`”的嵌入式安装方式；提供完整包集、精简包集及依赖表。说明如何更新版本、移除包和处理示例资源。
2. 在干净的 Unity 工程中验证两种安装：完整框架与最小 `Core + Resources + UI` 组合；确认缺少 Input 时仍能编译，并验证 Editor 不进入 Player。
3. 核查所有 `package.json` 依赖、asmdef 引用、`.meta`/GUID、`link.xml`、最低 Unity 版本字段和 TMP/UGUI 可选性；聚合包版本与子包版本各自清楚维护。
4. 若以后需要 Git URL 单入口体验，先在“重组单个可安装包”和“将多个包发布到 Scoped Registry”间评估，再单独立项；当前不虚构已有的远程自动解析能力。
5. 为新工程提供安装、升级、卸载、已知限制和版本兼容说明。

验收：一个不含 Yato 资源的全新 Unity 工程，仅按文档安装即可编译、运行核心示例；删除这些包后无残留编译引用。安装过程不依赖复制原仓库 `ProjectSettings`。

### IMP-04：示例独立化

现状线索：`YatoFrameworkDemo` 的 UI 入口仍使用原工程 `Assets/GameRes/UI/Example/Example.prefab`，并有 `GameStart` 与 `AppBootstrapSample` 两种需互斥的入口。

任务：

1. 把示例预制体、场景、数据和所需素材整理进可导入的 `Samples~`；保持现有 GUID 兼容或提供清晰迁移说明。
2. 分成 `BasicSample`（Bootstrap/Resources/UI/Scene 最短路径）与 `FullSample`（Settings/Audio/热更演示；Input 完成后再接入），避免互相竞争 `UIRouter.Configure`。
3. 给出 10～15 分钟可执行的 Quick Start：安装 → 导入示例 → 打开场景 → Play → 切换页面/场景 → 观察资源释放。
4. 示例中避免写死仅仓库内部存在的路径；非必需依赖不影响基础样例。

验收：独立工程通过 Package Manager 导入样例后直接运行；Console 无缺失脚本、预制体、asmdef 依赖与重复 Bootstrap 警告。

## 5. 阶段 C：生命周期与错误策略（P1）

### IMP-05A：明确事件与资源持有者

1. 修复 `Samples~/YatoFrameworkDemo/Scripts/AppBootstrapSample.cs`：保存 `LoadStarted`、`LoadCompleted` 的订阅委托，在 `Shutdown` 中对称取消；重复 `Initialize/Shutdown` 不叠加处理器。
2. 对框架及示例中的事件、回调、`CancellationTokenSource`、音频句柄、UI 缓存与场景资源进行一次所有权审查，优先使用已有 `DisposableBag`、`ResourceScope`，不新建通用生命周期框架。
3. 增加 Bootstrap 重启、切场景、Domain Reload 关闭、异步迟到结果和重复 Dispose 的回归用例。

验收：服务多次启停后，订阅数量和回调次数不增长；场景卸载后事件、UI 和音效不再访问已销毁对象。

### IMP-05B：资源 Provider 失败策略

现状线索：`ResourceServiceFactory.CreateBundleOrFallback` 在 manifest 不可用时告警并转为 `ResourcesProvider`，可能掩盖 Player 中正式资源缺失。

默认方案：

1. 将 `BundleRequired`（失败即报错）与 `AllowResourcesFallback`（明确授权降级）配置化；开发/预览可使用后者，正式 Bundle 模式默认前者。
2. 让失败诊断包含 manifest 路径、平台、Provider、失败阶段和原始异常；不吞掉关键错误。
3. 业务层决定是否显示修复提示、使用备选资源或退出，不将具体界面与修复策略塞进 Resources 层。
4. 对现有 `CreateProvider()` 的默认行为变化提供兼容说明、迁移示例与 CHANGELOG 记录。

验收：相同错误在严格模式下可定位、不会偷偷换 Provider；显式允许降级时行为稳定且有记录。

## 6. 阶段 D：D07 Input 与 UI 可选解耦（P2）

### IMP-06：完成 D07 可选薄适配

在前述稳定性工作完成后，按原架构文档 D07 的默认边界落地：

1. `com.yato.input` 独立 asmdef，依赖 Unity Input System；不安装它不影响其他包。由游戏提供 `InputActionAsset` 和 ActionMap 名称，不写死 Gameplay/UI 枚举。
2. 提供上下文启用/切换、统一禁用、订阅/退订生命周期管理；保持与 Unity Input System 官方 API 一致。
3. 提供交互式重绑定、取消和清理操作；复用官方 Binding Override JSON 接口，存储交给游戏层/Settings/Persistence。
4. 覆盖切换场景、重复进入 UI、重绑定取消、恢复默认与缺少 Input 包时的编译验证。

首版不做：重新实现底层按键轮询、本地多人输入管理、设备图标数据库或输入录制回放。

### IMP-07：验证 UI/PureMVC 耦合是否值得拆分

当前 UI 与 `Yato.Mvc`（PureMVC）存在直接依赖。先做**低成本可行性审核**：

1. 在样例中尝试一个不使用 PureMVC 的简易页面，记录哪些 UIRouter API 强制要求 `IFacade`/Mediator/Proxy。
2. 评估保留现状、提供可选适配器、拆分 UI Core/MVC Adapter 的源码与维护成本。
3. 仅当第二个独立项目确实需要不同 UI 架构、且适配能显著减少侵入时，另立重构任务；本轮不强制拆包或替换 PureMVC。

验收：形成明确结论和影响清单；不因为“理论上更通用”而增加不必要抽象。

## 7. 阶段 E：独立工程集成、人工验证与发布（不含 CI）

1. 在一个干净的 Unity 2022.3 LTS 工程和一个 Unity 6 工程分别导入框架，检查包解析、asmdef、样例和公开 API。
2. 在 Unity Test Runner **手动**执行 EditMode/PlayMode 全量测试；需要真实 AB 的测试先生成产物再运行，记录 PASS/FAIL/SKIP 数和原因。
3. 在 Windows Player 完成 Bootstrap → Settings → Audio → Single/Additive Scene → UI → HotUpdate → 退出清理端到端验证。热更可使用本地测试服务器，但生产 HTTPS 约束必须单独验证。
4. 在另一个小型 Unity 游戏工程里接入 Core、Resources、UI、App、Settings、Audio，记录“不得不绕过框架”的调用以及可删去的重复封装，作为下一轮改进依据。
5. 维护公开 API 变更记录、升级说明、包版本、示例、发布标签和人工验收报告；正式发布前确认没有把已知失败项标成通过。

**注意**：这里的本机 Test Runner、手动 Player Build 与人工记录不属于 CI，且不要求配置 GitHub Actions。

## 8. 发布门槛与交付清单

| 门槛 | 通过标准 |
|---|---|
| 热更安全 | 所有路径越界与清单异常用例被拒绝；无资源目录外写入 |
| 热更恢复 | 任意未完成更新不破坏上一成功版本；提交结果与取消状态一致 |
| 安装复用 | 两种 Unity 版本的空工程安装成功；基础样例不依赖原工程 Assets |
| 资源/事件生命周期 | 重复启停、取消、卸载和迟到结果不产生可观察泄漏或重复回调 |
| 错误与降级 | 正式模式缺 manifest 有明确错误；主动启用时才降级 |
| Input | 可选安装、上下文与重绑定正常，卸载后其他模块照常编译 |
| 发布验证 | EditMode/PlayMode、Windows Player、版本/升级文档均有人工记录 |

每个工作项交付时应包含：代码修改、对应自动化测试代码、手工验证结果、面向使用者的文档更新及必要的 CHANGELOG 条目。对于有意改变既有行为的修复，需写明兼容性影响和迁移方法。

## 9. 建议的提交拆分

1. `fix(resources): validate remote update manifest and download paths`（IMP-01）
2. `fix(resources): stage and commit hot updates with last-good rollback`（IMP-02）
3. `docs(packages): provide reproducible embedded UPM installation guide`（IMP-03）
4. `feat(samples): make basic and full demos self-contained`（IMP-04）
5. `fix(app): release sample scene subscriptions on shutdown`（IMP-05A）
6. `feat(resources): make fallback policy explicit`（IMP-05B）
7. `feat(input): optional Unity Input System adapter`（IMP-06）
8. `docs(ui): evaluate optional PureMVC adapter boundary`（IMP-07）
9. `test(release): record manual cross-version and Windows Player verification`（IMP-08）

执行时可以拆小 PR；其中 IMP-02 涉及旧缓存布局和公开行为，建议单独审核，不与 Input 开发混在一起。

## 10. 参考资料与既有文档

- [既有架构与路线图](YatoFramework_ARCHITECTURE_AND_ROADMAP_2026-10-30.md)：D01–D04 冻结边界、D05–D08 默认方案。
- [既有手动验证清单](YatoFramework_ManualVerification_2026-10-31.md)：后续按本计划增加热更恢复、独立安装和 Input 条目。
- [Unity 2022.3：Git dependencies](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-git.html)：Git 子目录安装与多包依赖限制。
- [Unity 2022.3：AssetBundle Download Integrity and Security](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetBundles-Integrity.html)：远程资源的完整性与安全边界。

**范围确认**：本文包含前次审核提出的全部非 CI 改进；CI/自动化工作流建设不在本计划内，也不作为以上阶段的阻塞条件。
