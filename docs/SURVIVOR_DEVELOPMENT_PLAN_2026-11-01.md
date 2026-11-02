# Yato Survivor 示例开发计划

日期：2026-11-01（Asia/Shanghai）。状态：M0–M5 的首版交付已完成。本文保留设计约束与验收目标；实际测试范围、环境限制和产物以[实施记录](SURVIVOR_IMPLEMENTATION_2026-11-01.md)为准，不将计划中的每个故障注入场景都视为已实测。

最终实施决策：流程协调直接放在组合根 `SurvivorGame`，未单独建立 `SurvivorFlowCoordinator` 类；仍先等待停止与卸载，再切换 Flow。奖励与经验参数统一由 `RewardTable` 管理。界面采用随资产合法分发的 Liberation Sans，首版为英文；音乐与音效已程序合成并接入 AudioService。独立消费工程和可直接运行的 Windows 目录已经生成，框架改进候选保留供后续统一修补。

## 1. 目标与当前基线

在当前 `E:/YatoUIFramework` 工程内实现一个能实际游玩的 2D 像素幸存者示例，展示现有框架的完整接入与退出过程。游戏代码单向依赖框架；开发中发现的通用问题持续写入[问题记录](SURVIVOR_FRAMEWORK_ISSUES.md)，供后续统一评估和修补。

已核对的基线：

- Unity `6000.5.6f1`、Framework `0.6.0`；使用当前已安装包，不在示例开发中顺带升级框架或 Unity 包。
- 已导入 49 张 PNG、152 个 Sprite，清单见 [Art/README](../Assets/GameRes/Survivor/Art/README.md)。导入通过不代表动画、Prefab 或玩法已经完成。
- Flow、Config、Pooling、App、UI、Resources 已具备；当前宿主也已安装可选 Input、UI.Input、Save，输入模式为 Both。
- 当前 Build Settings 启用了不存在的 `Assets/Scenes/SampleScene.unity`；须在 M1 配好有效的示例入口再进行构建验收。
- 仓库原有测试结果作为历史基线；本示例需独立产生自己的测试与 Player 证据，不沿用历史结果宣称通过。

## 2. 首版玩法范围

| 项目 | 首版约定 |
|---|---|
| 运行平台 | Windows 单机；Editor 与 Windows Mono Player 都可独立启动 |
| 一局流程 | 主菜单 → 加载 → 战斗 → 升级选择 / 暂停 → 胜利或死亡结算 → 再来一局 / 返回菜单 |
| 时长与地图 | 默认 10 分钟，可由配置调整；一个有边界、可重复铺设地砖的开放地牢场地 |
| 玩家 | 一名骑士；WASD / 方向键 / 手柄左摇杆移动，待机和跑动动画，自动攻击 |
| 敌人 | 骷髅、持刃骷髅、幽灵头骨、吸血鬼；分别体现基础、耐久、快速、精英差异，数值与刷新节奏由配置控制 |
| 首批武器 | 剑的近身范围攻击、弓的定向投射物、蓝法杖的自动索敌投射物；先完成一把，再扩展三类 |
| 成长 | 杀敌掉经验，吸取经验升级，三选一升级；武器等级、伤害、冷却、移速、生命、拾取范围等配置化 |
| 掉落 | 经验、金币、回复药水、奖励宝箱；金币先用于本局统计及历史记录 |
| 界面 | 主菜单、加载反馈、HUD、升级选择、暂停、设置、结算与错误反馈 |
| 设置与记录 | 音量、输入重绑定与显式保存；历史最佳时间、击杀、金币等记录；不做战斗中途续存 |

斧和绿法杖作为后续内容扩展，不阻塞首版验收。首版不包含联机、商业化、程序化房间、复杂寻路或局外养成。音频先完成 `AudioService` 接入；现有美术目录不包含音频，实际试听须有明确可用的音频资产后单独验收。

UI 文本集中管理。现有字体资源尚未证明覆盖中文；先检查所需字符，缺字记为资源问题，使用可用字符保证首版可读，不复制系统字体作为发布资源。

## 3. 必须遵守的框架接入约定

| 责任 | 使用现有能力 | 示例侧约束 |
|---|---|---|
| 启动与关闭 | `GameBootstrap`、`IAppService`、`RuntimeHost` | 仅示例入口注册服务；`Initialize/Shutdown` 保持同步与幂等，异步准备放到 Flow；不新增全局 GameManager 单例 |
| 应用流程 | `FlowController`、`IFlowState`、`TaskBridge` | 所有状态切换走 Flow；正确处理 Success / Busy / Canceled / Failed，不用第二套状态机替代 |
| 场景 | `GameBootstrap.Scene`、`ISceneInitializer` | 加载、卸载走 SceneService，只有 Ready 后开放玩法；运行时代码不直接调用 SceneManager 加载 |
| 资源 | `IResourceService`、`AssetKey`、`IAssetHandle`、`ResourceScope` | 运行时异步加载、明确句柄所有者；不调用 Resources.Load、AssetDatabase 或直接加载 AssetBundle |
| 配置 | `ConfigCatalog`、`ConfigTable<T>`、`ConfigRecord`、`ConfigService` | 玩法参数和表间关系进入配置校验；不修改配置行来存放本局血量、武器等级等状态 |
| 反复生成的实体 | `ResourceInstancePool`、`PooledInstance`、`IPoolCallbacks` | 敌人、弹体、拾取物、短特效使用有界实例池；不自建通用对象池，不在战斗热路径反复 Instantiate / Destroy |
| 暂停 | 唯一的 `PauseService` | 暂停、升级、失焦各持自己的令牌；禁止业务直接改 `Time.timeScale`；UI 动画用非缩放时间 |
| UI | `UIRouter`、`UIPageView`、`UIPageMediator<T>`、`UIPageProxy`、`ComponentBinder` | 页面按框架生命周期创建 / 开关 / 释放，使用生成绑定；不手工搭建另一套页面栈或绕路由管理 Canvas |
| UI 与输入 | `InputContextService`、`UIInputCoordinator`、`UIInputProfile`、`UIPageFocus` | 使用运行时 InputActionAsset 副本；HUD 不接管输入，弹窗接管；先释放协调器再释放输入服务 |
| 设置与存档 | `SettingsService<T>`、`SaveService<T>`、`FileStore` | 设置 Apply 与 Save 分开；历史记录独立 DTO；损坏存档不直接覆盖，不自动恢复备份 |
| 音频 | `AudioService`、`AudioBus` | 游戏层将设置变化同步到总线；不另写 AudioManager 或私有音效池 |
| 事件与诊断 | `IEventBus`、`DisposableBag`、`IDiagnosticSource`、`DiagnosticRegistry` | 使用现有事件总线和订阅释放工具；局内事件带 RunId；不经全局通知逐个广播每帧弹体移动 |
| 显式退出 | `ApplicationExitCoordinator` | 用户退出时等待保存与清理，再走框架关闭；不把系统强杀当作可保证完成的保存路径 |

允许在示例层编写伤害、索敌、刷新、升级选择、Sprite 动画等游戏业务。Unity 的 Transform、SpriteRenderer、2D 查询和 UGUI 是实现这些业务的基础能力，不属于替换框架服务。

`ResourcesServiceHost` 是 Resources-only Starter，不作为此示例的正式资源入口。示例组合根调用现有 `ResourceServiceFactory`，向 UI 和其他业务注入同一个 `IResourceService`。Editor 默认 Provider 用于开发；强制 Bundle 和 Player 用于验证真实资源链，禁止用隐式 Resources 降级掩盖打包问题。

## 4. 工程组织与依赖方向

首版在宿主 Assets 中开发，便于直接查看与运行；本轮不创建游戏专用框架包，不把业务写进现有 `Packages/com.yato.*`。

```text
Assets/SurvivorSample/
  Domain/                         # Yato.Survivor.Domain：纯 C# 规则和数据，不引用 Unity / 框架
  Runtime/                        # Yato.Survivor：组合根与 Unity / 框架集成
    Bootstrap/  Flow/  Session/
    Config/  Gameplay/  Presentation/
    Presentation/Generated/       # 现有代码生成器产出的绑定、AssetKeys、UIPages
  Editor/                         # Yato.Survivor.Editor：制作、引用校验、构建辅助，仅 Editor
  Tests/EditMode/  Tests/PlayMode/ # 独立测试程序集
  Scenes/SurvivorBootstrap.unity
  Scenes/SurvivorArena.unity

Assets/GameRes/
  Survivor/Art/                   # 已导入原图，保留图像内容与 GUID
  Survivor/Presentation/          # Sprite 集合与动画数据资产
  Survivor/Prefabs/               # 玩家、敌人、投射物、拾取物、效果
  UI/Survivor/                    # 页面 Prefab，沿用 ui/survivor/... key
  Config/Survivor/                # ConfigCatalog 和各具体表资产
  Audio/Survivor/                 # 有可用音频后添加

docs/                            # 计划、问题记录和阶段验证报告
```

依赖方向：`Yato.Survivor → Yato.Survivor.Domain + 当前使用的 Yato 运行时程序集`。Editor / Tests 向运行时依赖；框架程序集不反向引用 Survivor。`Domain` 不引用 `UnityEngine`；运行时集成程序集显式声明 Input、UI.Input、Save、MVC 及 Unity UI / InputSystem 等实际使用的依赖。

生成代码放在示例 Runtime 程序集内部，避免默认 `Assembly-CSharp` 反向引用和 Generated / UI 之间的循环依赖。现有生成器的输出目录是 EditorPrefs；示例制作工具设置后恢复原值，不能改变其他示例的持久设置。

场景先使用 Build Settings 中的内置场景，由 SceneService 加载；不与 `survivor` 的美术、Prefab 混装到同一 AssetBundle。其他资源沿用 `BundleNaming`，不自定义第二套 key / bundle 命名规则。游戏专有资源的首次 Bundle 验证从 M1 开始，不留到最后。

## 5. 流程与一局战斗的生命周期

外层状态使用 `Boot → MainMenu → Run → Results`，并提供 `Error`。再来一局为 `Results → Run`；返回菜单为 `Results → MainMenu`。加载是状态进入过程的 UI，不另建会在切换时释放战斗资源的 Loading 状态。

- `Boot`：同步服务已初始化后，异步加载配置目录、表现资源、输入资产和必要 UI；成功的常驻资源转交应用范围。Boot 的临时范围不持有进入菜单后仍需使用的唯一句柄。
- `MainMenu`：显示菜单和历史记录，接收开始、设置、退出意图。
- `Run`：通过 SceneService Additive 加载 Arena；初始化器创建 RunSession、加载必要资源并预热实例池；Ready 后启动刷新、计时和战斗。
- `Results`：读取离开战斗前复制的纯数据结算快照，异步保存历史记录，提供重开与返回。
- `Error`：显示可操作的失败信息；清理已加载但未 Ready 的场景和半初始化会话，提供重试、返回或退出。

暂停、升级、宝箱奖励、局内设置均在 `Run` 状态内处理。使用 UI 页面加暂停令牌，不切换外层 Flow 状态，保留同一局实体、资源和计时。连续多级升级排队处理，整个选择过程持有暂停令牌，不能在两个弹窗之间意外恢复战斗。

输入分为 Gameplay 与 UI Action Map。暂停弹窗 `AllowBack=true`；升级选择 `AllowBack=false`，必须选择有效结果。HUD `CaptureInput=false`；菜单和模态页面 `CaptureInput=true`。Gameplay/Pause 打开暂停页，UI/Cancel 由协调器关闭可返回页面，禁止同一次 Escape 同时打开和关闭页面。

`IFlowState.Exit` 当前是同步接口。示例使用薄的 `SurvivorFlowCoordinator` 串行组织退出步骤，不在其中再实现状态机：

1. 拒绝重复用户意图，冻结本局输入与更新，复制结算快照。
2. 请求取消局内异步任务，等待实际结束；关闭局内 UI、解除订阅、归还租约。
3. `await SceneService.UnloadAsync(arenaContext)`；确认场景卸载和资源释放。
4. `await FlowController.SwitchAsync(target)`；`Run.Exit` 仅同步、幂等地兜底清理。

场景卸载失败时保持已停止的会话并显示错误，不伪装成已经进入结算或开始下一局。进入 Run 失败后也必须显式清理残留场景。Flow / Scene 的 Busy、取消和失败全部检查并记录；不能 fire-and-forget 卸载后立即重开。

## 6. 资源所有权与实例池

| 对象 | 唯一主要所有者 | 释放时机 |
|---|---|---|
| 资源服务、常驻 ConfigService、输入、设置、音频、Save、Pause、Flow | 应用服务 / 应用 ResourceScope | 示例关闭，按依赖逆序释放 |
| RunSession | Arena 的 `SceneContext.Resources` | 场景卸载；会话 Dispose 可重复调用 |
| 局内池、表现集合句柄、任务取消源、局内订阅 | RunSession 使用现有 ResourceScope / DisposableBag 管理 | 停止或释放会话 |
| UI 页面 Prefab 句柄与缓存 | UIRouter | 页面销毁 / 缓存清理；会话仅持关闭权，不再重复管理加载句柄 |
| 单次租用的 PooledInstance | 对应实体登记表 | 死亡、超时、命中消失、离场或局结束 |
| 暂停令牌 | 发起暂停的页面 / 功能 | 页面关闭、选择完成、失败回滚或会话结束 |

资源完成后先检查取消和会话有效性，再使用结果；局部句柄在转交范围前失败必须释放。ResourceScope 对迟到结果会立即 Dispose，不能 Track 后继续假设句柄仍有效。

`PooledInstance` 是值类型，保存为具体结构体，不装箱成 IDisposable 逐个放进 ResourceScope；只将池本身登记到范围。敌人、弹体、拾取物、效果分别配置容量、预热量和满池策略。满池时延后敌人刷新、拒绝该次投射或效果，并计数；金币和经验奖励仍保留在业务待发队列中，不能悄悄丢失。

`TryRent` 在返回租约前会激活对象并调用 OnRent。组件的 OnRent / OnEnable 只复位，不启动依赖出生位置和目标的逻辑；调用方立即传入本次生成参数，完成后才允许移动、伤害或碰撞。OnReturn 清空目标、事件、计时器和命中记录。命中目标标识包含本次生成代次，防止对象复用后误伤、重复发奖。

## 7. 配置、表现与玩法实现约定

具体配置表使用类名与文件名一致的 ScriptableObject 类：

| 表 | 主要字段与校验 |
|---|---|
| Character | 生命、移动速度、拾取半径、初始武器 ID；数值范围与武器引用 |
| Enemy | 血量、速度、接触伤害、经验 / 掉落、Prefab key、表现 ID；正值、资源引用 |
| Weapon | 攻击方式、伤害、冷却、射程、投射物和表现 ID；冷却非零、参数与攻击类型匹配 |
| Projectile | 速度、寿命、命中半径、穿透、Prefab key；有效范围和资源存在性 |
| Upgrade | 条件、权重、最大等级、属性改动、武器解锁 / 升级 ID；权重与跨表引用 |
| Stage / SpawnWave | 时长、场地范围、各时段刷新组合、数量及上限；时序、敌人 ID、容量预算 |
| Drop / Experience | 奖励类型、概率、数量、升级经验曲线；概率范围、增长约束 |

跨表校验使用 `ConfigValidationContext.Require<T>`；游戏资源与表现引用实现 `IProjectValidationSource`，接入现有 Project Doctor 和构建预检。玩法运行数据独立为角色状态、武器状态、敌人状态、经验等级和本局统计，不写回配置资产。

现有资源接口没有按名称选择 Sprite 子资源的契约。创建示例专用 `SurvivorSpriteSet` / 动画数据 ScriptableObject，用 Unity 序列化引用保存 Sprite 帧与图标，再通过 `IResourceService.LoadAsync<SurvivorSpriteSet>` 加载。由该集合的句柄保住帧依赖，所有使用者先结束再释放集合。运行时禁止用 Sprite.Create、Resources.LoadAll 或 AssetDatabase 重新切图。

Prefab key 使用现有生成器输出。配置目录、SpriteSet 等非 Prefab 资产的 key，由示例 Editor 制作步骤调用现有 `BundleNaming.GetAssetKey` 填入并校验，不在运行时拼接字符串；现有生成器仅收集 Prefab 的限制记录为改进候选。

战斗使用一个可停止的游戏更新入口：移动、刷新、攻击、投射物、伤害、死亡、奖励、经验与表现更新有明确顺序。伤害与死亡幂等；UI 意图经 Mediator 调用应用用例，Proxy 只投影模型，不另存第二份权威战斗状态。HUD 按变化或限频刷新，实体移动不走 PureMVC 通知。

纯规则接收可控时间步长与随机源，便于复现升级候选与伤害测试；不宣称 Unity 场景或物理是跨设备确定性的。热路径避免 LINQ、每帧异步桥接和创建临时集合；先用有界列表 / 缓冲区完成目标规模，再依据测量优化。

## 8. 分阶段实施与阶段门槛

| 阶段 | 交付内容 | 完成门槛 |
|---|---|---|
| M0 规划（本轮） | 本文、问题登记、README 入口 | 责任映射、所有权和验收标准明确；不产生玩法已完成的表述 |
| M1 框架接入骨架 | asmdef、Bootstrap / Arena、服务组合根、Flow、空 RunSession、主菜单 / HUD、Input、首批配置和 SpriteSet、资源 key 制作 | 从专用入口可启动；开始 / 返回连续 10 次不残留场景、租约、暂停令牌或订阅；首批资源在 Editor 默认和 Bundle 模式一致加载；独立消费工程验证初始链路 |
| M2 最小可玩闭环 | 玩家移动、基础骷髅刷新与追踪、第一把剑、伤害 / 死亡、经验、三选一升级、失败结算和重开 | 不依赖调试按钮，可实际完成移动→杀敌→拾取→升级→死亡→重开；支持正常与失败 / 取消清理 |
| M3 首版内容 | 4 类敌人、3 类武器、完整阶段刷新、金币 / 药水 / 宝箱、胜利条件、动画和命中特效 | 可玩完默认 10 分钟；数据调整通过 Config；连续升级、精英、死亡同帧竞争等边界结果正确 |
| M4 页面与持久化 | 暂停 / 设置 / 结算页面、键鼠和手柄焦点、重绑定、Settings、历史记录 Save、AudioService 接入 | 暂停叠加可恢复；改键与设置可保存重载；损坏文件明确反馈并支持显式恢复；无音频资产的项单列待验收 |
| M5 集成与交付 | 完整回归、生命周期与诊断、真实 Bundle、Windows Mono Player、独立消费工程、运行手册和阶段报告 | 下节门槛全部有新证据；每条问题更新状态；失败 / 跳过 / 环境限制分开记录 |

各阶段顺序实施。每阶段结束报告实际完成内容、框架 API 使用情况、测试证据和新问题；未达到阶段门槛不标记完成。M1 的基础消费验证和 Bundle 加载会提前发现目录、程序集或资源依赖问题，M5 再验证完整游戏。

## 9. 验证与完成标准

- EditMode：配置重复 ID / 无效数值 / 缺失引用，伤害与死亡只结算一次，经验跨多级，升级候选无重复 / 满级过滤 / 无候选兜底，阶段刷新边界，结算 DTO 与存档版本校验。
- PlayMode：真实 SceneService Ready 与失败清理、资源句柄释放、池复用与满池、重复输入单次开始、暂停叠加、弹窗焦点恢复、连续升级、退出过程中迟到异步结果、结算保存后重开。
- 生命周期：至少 100 次开始 / 结束会话，回到“主菜单稳定基线”；战斗资源、活动租约、局内订阅与暂停令牌归零。菜单 UI 和常驻资源允许非零，以记录的基线比较，不宣称整应用资源全零。
- Editor / Bundle 一致性：验证配置目录、Prefab、SpriteSet 和所有命名 Sprite 帧；非法 key 应失败，不能只看 Editor 兜底能显示。完整验收必须包含强制 Bundle 和原生 Player。
- 静态约束：框架不反向引用示例；运行时无 UnityEditor、直载资源 API、直切场景、直接修改 timeScale 或替代框架的服务实现；生成 key 与 BundleNaming 一致。现有 API 检查器之外再审查此示例范围的调用。
- 独立消费：新建干净工程，只装所需框架包并导入示例代码和资源，以有效的 Bootstrap 构建场景运行；不依赖源工程 Library、TMP 修改、其他示例初始化或本机 Unity MCP。
- 真实输入与显示：键鼠操作、手柄焦点、不同窗口尺寸、动画与拾取反馈分别记录；模拟设备通过与真实硬件通过分开。UI 验收使用真实页面，不以 OnGUI 或测试面板替代最终页面。
- 性能：以配置好的 200 个存活敌人、100 个投射物、200 个拾取物为首轮压力场景，Windows Mono Release，固定分辨率 / 质量，预热后采样至少 60 秒并记录硬件、平均和 P95 帧耗时、GC、池峰值与满池次数。16.7 ms P95 为初始目标，必须实测后判断；不以框架空场景基准代替游戏数据，不承诺整游戏零分配。
- 构建：Project Doctor 和配置校验无错误，Windows Bundle 与 Mono Player 可从 Bootstrap 独立运行，并完成一局、重开、返回、保存与退出。IL2CPP 仅在本机模块和工具链具备并实测后单列通过。

自动测试覆盖有实际回归价值的规则与生命周期，不为素材复制、简单属性转发编写镜像测试。不新增 CI；测试、构建、Profiler 和截图证据保存在本地 Logs / Build，仓库文档汇总结果和复现入口。

## 10. 框架问题的记录与处理

问题表见 [SURVIVOR_FRAMEWORK_ISSUES.md](SURVIVOR_FRAMEWORK_ISSUES.md)。记录必须包含来源位置、触发场景、期望 / 当前行为、影响、现有 API 下的处理、验证程度和建议验收。

示例开发不修改框架实现。实现使用已有能力；合理的游戏适配器留在示例，通用基础设施缺口留在记录。若某项确实阻断正确实现且没有符合现有契约的处理路径，记录最小复现和受影响阶段，明确报告阻塞；不得静默复制一套加载器、池或路由器绕过。

项目接入修正与框架修补分别标记。例如修正示例的构建列表、移动不参与运行的审计文件是项目接入工作；扩展 IResourceService、Flow 接口或框架代码生成器属于后续统一评估项。所有未实现的阶段和待验证问题保持开放状态。
