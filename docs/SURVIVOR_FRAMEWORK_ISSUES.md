# Survivor 接入中的框架问题记录

建立日期：2026-11-01（Asia/Shanghai）。本记录随示例开发持续更新。条目分别标明静态检查与游戏运行验证状态；未修改框架运行时与编辑器实现。2026-11-02 提交前调整了框架热更冒烟测试的多 Bundle 夹具兼容性，见提交前验证摘要。

文档核对：2026-11-02。已将最终规则、集成、完整战斗、设置 / 输入、独立构建与压力日志整理到[实施记录](SURVIVOR_IMPLEMENTATION_2026-11-01.md)和[验证摘要](verification/SURVIVOR_2026-11-01.json)。示例适配通过不代表框架 API 限制已被统一修补。

## 记录规则

- `F-*`：框架 API 限制、工具覆盖或设计改进候选；限制不自动等于缺陷。
- `I-*`：宿主工程、资源组织或环境的接入问题，不作为框架缺陷统计。
- 状态区分：已确认接口限制、静态风险待复现、接入待处理、已复现缺陷、待统一评估、已修复待验证、已验证关闭。
- 新条目必须写明：发现阶段、源码证据、触发步骤、期望 / 当前行为、对示例影响、当前处理、后续修补建议与验收。未运行的复现步骤只写为计划，不声称测试通过。
- 后续框架修改应保留最小复现、兼容性影响和回归证据；不能仅因示例有一份适配代码就要求上升为通用框架能力。

## 概览

| ID | 分类与状态 | 影响 / 优先级 | 当前实施路径 |
|---|---|---|---|
| F-001 | 已确认接口限制：按名称加载 Sprite 子资源 | 资源与表现接入；优先验证 | 示例 ScriptableObject 引用帧，通过资源服务加载该对象 |
| F-002 | 已确认接口限制：Flow 退出同步 | 退出 / 重开时序；优先验证 | 业务协调器先 await 停止与卸载，再切换 Flow |
| F-003 | 已确认工具覆盖限制：禁用 API 检查 | 框架约束验收；一般 | 现有检查加示例范围调用审查 |
| F-004 | 静态风险待复现：Editor 按叶子名兜底 | 开发 / Player 一致性；优先验证 | 使用完整 key，增加同叶子名和错误 key 的一致性验收 |
| F-005 | 已确认工具覆盖限制：AssetKeys 仅生成 Prefab key | 配置 / 表现资源制作；一般 | 示例 Editor 通过现有 BundleNaming 填充非 Prefab key |
| F-006 | 已复现接入限制：Gameplay 与可点击 HUD 的输入动作共存 | 原生暂停按钮；已做示例适配 | 在 Gameplay 上下文仅额外启用 UI/Point 与 UI/Click |
| I-001 | 已验证关闭：原启用的构建场景不存在 | 已通过 Doctor 与首次 Player 构建 | 有效 Bootstrap / Arena 已加入构建列表 |
| I-002 | 已验证关闭：审计 JSON 已移出打包目录 | 已验证不在 Bundle 收集中 | docs/SurvivorArtManifest.json，原 PNG / meta 保留 |

## F-001：资源接口没有命名 Sprite 子资源契约

- 发现阶段：M0 规划；状态：已确认接口限制。表现集合适配已通过真实配置校验、强制 Bundle 完整战斗与独立 Player 构建；逐项 Sprite 名称 / 矩形的完整 Player 对照仍不作已验收声明。
- 证据：[IResourceService.cs:21](../Packages/com.yato.resources/Runtime/Abstractions/IResourceService.cs#L21) 以一个 AssetKey 和类型加载单个对象；[EditorAssetProvider.cs:65](../Packages/com.yato.resources/Runtime/Providers/EditorAssetProvider.cs#L65) 使用 LoadAssetAtPath；[BundleProvider.cs:213](../Packages/com.yato.resources/Runtime/Providers/BundleProvider.cs#L213) 使用 LoadAssetAsync。没有指定子 Sprite 名称或返回子资源集合的公开契约。
- 触发场景：骑士待机 / 跑动图各 6 帧，FantasyIcons 原图切分了 12 个命名 Sprite，需要分别引用指定帧。
- 期望 / 当前：游戏需要稳定取得指定帧；当前无法通过“同一原图 key + 子资源名”表达此请求，不能假设 LoadAsync<Sprite> 会返回所需的某一帧。
- 当前处理：示例 `SurvivorSpriteSet` 序列化引用 Sprite[] / 图标，资源服务加载集合，集合句柄随应用 / 会话范围释放。此类表现数据属于游戏资产，不新增资源 Provider。
- 统一评估候选：是否需要 SubAssetKey 或有明确所有权的子资源 API；仅当第二个消费场景也需要时再扩展通用契约。
- 后续验收：Editor 与 Bundle / Player 中逐项校验所有所需 Sprite 的名字、矩形和非空引用；退出后句柄与 Bundle 引用回到稳定基线。

## F-002：Flow.Exit 不能等待异步清理

- 发现阶段：M0；状态：已确认接口限制，当前契约本身不判为缺陷。
- 证据：[FlowController.cs:28](../Packages/com.yato.flow/Runtime/FlowController.cs#L28) 的 Exit 返回 void；[FlowController.cs:124](../Packages/com.yato.flow/Runtime/FlowController.cs#L124) 调用旧状态 Exit 后立即释放其 ResourceScope。
- 触发场景：结束战斗需要取消并等待任务、关闭带过渡的 UI、卸载 Arena，再进入结算或重开。
- 期望 / 当前：不能将异步卸载放进同步 Exit 后直接开始下一状态；框架当前不提供 ExitAsync，也不自动回滚失败场景。
- 当前处理：协调逻辑实际位于 `SurvivorGame`，先串行等待 RunSession 停止和 SceneService 卸载，再调用 Flow.SwitchAsync；未另建计划中的 `SurvivorFlowCoordinator` 类。Run.Exit 保持同步幂等兜底。Pause / Upgrade 保持在 Run 内，避免状态切换释放战斗资源。百轮生命周期与路由集成已通过；下列故障注入仍为后续验收目标。
- 统一评估候选：具备取消 / 超时边界的可选异步退出阶段，保留现有接口兼容；须先用实际示例判断协调器复杂度是否值得上升为框架能力。
- 后续验收：退出中连续点击重开、卸载失败、初始化失败、迟到资源、会话中取消、嵌套暂停。旧任务与资源未结束前不开放下一局。

## F-003：现有禁用 API 检查不足以证明严格接入

- 发现阶段：M0；状态：已确认工具覆盖限制。
- 证据：[ApiUsageValidator.cs:15](../Packages/com.yato.editor/Editor/Tools/ApiUsageValidator.cs#L15) 的正则只匹配 Resources.Load / LoadAsync / LoadAll；第 23 行仅扫描 Assets；第 51 行起输出 Warning。类注释提到 AssetDatabase，但正则未检查该 API，也不覆盖场景直切或 timeScale 写入。
- 触发场景：示例要求资源、场景、暂停和对象池严格使用框架；菜单显示无 Resources.Load 不足以证明这些约束均满足。
- 当前处理：运行原有检查，同时对 Survivor Runtime 做有范围的源码 / 调用审查，验证程序集边界、运行时 UnityEditor、场景加载、时间倍率和实体生成路径。Editor 资产制作与测试夹具属于合法不同上下文，不能简单全局封禁名称。
- 统一评估候选：可配置的规则、明确允许范围和可供构建门禁使用的结构化结果；不在示例里复制一套通用扫描框架。
- 后续验收：后续如修补，应使用故意违规与合法 Editor / 测试调用的对照夹具；确保命中和误报都可解释。

## F-004：Editor 兜底搜索可能掩盖错误 key

- 发现阶段：M0；状态：静态风险待复现，尚未登记为已复现缺陷。
- 证据：[EditorAssetProvider.cs:132](../Packages/com.yato.resources/Runtime/Providers/EditorAssetProvider.cs#L132) 使用叶子名和类型搜索；第 139 行返回首个文件名匹配的 path，该分支没有验证完整相对 key，也没有对多个同叶子结果报告歧义。
- 计划复现：创建两个目录下的同名 Prefab，传入错误目录的完整 key，比较 Editor 默认 Provider 与 BundleProvider 的结果；测试夹具放在测试目录并及时清理。
- 期望 / 静态观察：错误或有歧义的资源映射应有可解释的失败；当前分支可能让 Editor 加载成功而 Player 按 manifest 失败，或取到非预期资源。
- 当前处理：示例使用生成或序列化的完整 key；M1 就验证真实 Bundle，制作工具依据 BundleNaming 检查 key 与路径一致，不能以 Editor 兜底成功作为资源验收。
- 统一评估候选：保留兼容的显式宽松模式、严格完整 key 模式与歧义诊断；先复现再判断默认策略是否调整。
- 后续验收：同叶子名、错误目录、类型不匹配、正确完整 key，分别验证开发模式与 Bundle 的行为和错误信息。

## F-005：AssetKeyGenerator 当前只覆盖 Prefab

- 发现阶段：M0；状态：已确认工具覆盖限制。
- 证据：[AssetKeyGenerator.cs:27](../Packages/com.yato.editor/Editor/CodeGen/AssetKeyGenerator.cs#L27) 起从 CollectPrefabs 构建 AssetKeys；该收集方法使用 `*.prefab`。ConfigCatalog、SpriteSet、音频等资产不进入这份 key 常量表。
- 触发场景：本示例既需要 UI / 实体 Prefab，也需要配置目录、表现集合和音频 key；不能假设所有资源常量都已生成。
- 当前处理：Prefab 继续走原生成器；其他资产由示例 Editor 制作入口调用现有 BundleNaming.GetAssetKey 填入启动配置 / 数据资产，并在构建前检查。运行时使用这份明确配置，不建立新的路径规则。
- 统一评估候选：AssetKeys 收集范围与 BuildableExtensions 一致，UIPages 单独筛选 UI Prefab；评估命名冲突、生成兼容性和输出目录后统一修改。
- 补充实测：首次使用生成器时，UIPages 也为 Enemy / Projectile 等非 UI Prefab 生成了页面定义。示例仅引用实际 UI 定义，没有使用这些多余项；建议统一修补时把 UI 页面的收集条件与通用资源 key 分开。
- 后续验收：同目录多后缀资源、重复标识、Prefab / ScriptableObject / 音频、非 UI Prefab 过滤、无变化不改写、独立消费程序集编译。

## I-001：Build Settings 引用缺失场景

- 发现阶段：M0；状态：已验证关闭。已配置有效示例场景，首次 Doctor 与 Windows 构建通过。
- 历史证据：示例接入前的 Build Settings 启用 `Assets/Scenes/SampleScene.unity`，当时 Test-Path 为 False。当前 [EditorBuildSettings.asset](../ProjectSettings/EditorBuildSettings.asset) 已改为有效 Bootstrap / Arena。Project Doctor 对不存在的启用场景报错，见 [ProjectDoctor.cs](../Packages/com.yato.editor/Editor/Tools/ProjectDoctor.cs)。
- 影响：源工程正常 Player 构建预检无法通过；不是 Survivor 游戏代码或框架 SceneService 的缺陷。
- M1 处理：创建真实的 SurvivorBootstrap / SurvivorArena；去掉已确认不存在的旧条目，保留其他有效条目，保证示例 Bootstrap 为示例构建入口。构建脚本使用明确的场景列表。
- 关闭标准：Project Doctor 无缺失场景错误；从专用 Bootstrap 启动并通过 SceneService 进入 Arena，Player 独立启动成功。

## I-002：素材审计清单会被当前规则收集进 Bundle

- 发现阶段：M0；状态：已验证关闭。审计 JSON 已移至 docs，Bundle 收集确认不再包含 ArtManifest。
- 证据：原 `Assets/GameRes/Survivor/Art/ArtManifest.json` 位于 GameRes 内（现为 [SurvivorArtManifest.json](SurvivorArtManifest.json)），只用于来源 / 哈希 / 切片审计；[BundleNaming.cs:28](../Packages/com.yato.editor/Editor/Build/BundleNaming.cs#L28) 包含 `.json`，CollectBuildableAssets 默认递归收集，并没有针对该清单的排除规则。
- 影响：默认打包会把不参与运行的清单也作为 TextAsset 收集，包含本机来源路径；游戏并不需要加载它。
- M1 处理：将审计 JSON 移至 docs 下并更新 Art README 的引用，PNG 与现有 meta 保持不变。游戏运行的 ConfigCatalog 仍留在正常资源目录。
- 统一评估候选：是否需要清晰的打包排除约定；本例移动文档即可解决，不要求先修改框架打包器。
- 关闭标准：Bundle 收集 / manifest 不再包含审计清单；素材来源与哈希记录仍可查，现有 49 张原图和 Sprite 引用不受影响。

## F-006：独占基础上下文会关闭可点击 HUD 的指针动作

- 发现阶段：M5 首次原生 Player 交互；状态：已复现接入限制，示例已适配。最终集成与设置 / 输入专项通过，最新独立原生构建已启动和退出；日志不足以单独证明下面全部原生鼠标验收条件，保持待人工复核。
- 证据：UIInputCoordinator.Refresh 在没有 CaptureInput 页面时调用 InputContextService.EnableContext(baseMap)，后者默认独占启用 Gameplay，关闭 UI map 的 Point / Click。原生菜单和升级卡能点击，而战斗 HUD 暂停按钮未响应；仅调用 Button.onClick 的生命周期测试无法发现这一点。
- 期望 / 当前：HUD 不应阻止移动，同时仍需接受鼠标按钮操作；协调器目前没有可配置的“基础上下文保留动作”集合。
- 当前处理：示例在页面开关与进入战斗后检查 Gameplay 是否启用，仅额外启用运行时副本的 UI/Point 与 UI/Click；不启用 UI/Cancel，避免与 Gameplay/Pause 的同一次 Escape 重叠。不修改源 InputActionAsset 或另建输入服务。
- 已有验证：虚拟键盘移动、Escape 暂停 / 恢复、HUD Point / Click 开启、Gameplay 中 UI/Cancel 关闭均通过，见 Logs/Survivor-Settings-Input-Verification.json。
- 统一评估候选：协调器的基础上下文与非模态 UI 指针动作组合配置，明确导航 / Cancel 的独占语义。
- 关闭标准：最新原生 Player 鼠标 HUD 暂停可操作，弹窗期间移动不生效，关闭后移动和 HUD 指针恢复；保持原有 UI.Input 消费侧行为。

## 后续追加模板

```text
ID / 标题：
发现阶段 / 日期 / 状态：
源码位置或日志证据：
触发步骤（已执行还是待执行）：
期望行为 / 当前行为：
影响范围与受影响里程碑：
是否存在符合当前框架契约的实施路径：
当前处理及代价：
统一修补建议与兼容性影响：
回归与关闭标准：
实际验证结果 / 证据路径：
```
