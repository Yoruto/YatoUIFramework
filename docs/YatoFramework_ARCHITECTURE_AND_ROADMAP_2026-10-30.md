# YatoFramework 框架架构与后续开发计划

> 文档类型：架构设计与实施规划草案  
> 整理日期：2026-10-30  
> 目标仓库：[Yoruto/YatoUIFramework](https://github.com/Yoruto/YatoUIFramework)  
> 目标环境：Unity 2022.3 LTS 起，优先验证 Windows Player；兼容 Unity 6 作为验证目标  
> **状态说明：D01–D04 为已确认并冻结的设计；D05–D08 为参考成熟实践形成的推荐默认方案，尚待整体审核。本文不代表代码已实现或通过测试。**

## 1. 目标与非目标

### 1.1 目标

YatoFramework 是可在多个独立 Unity 游戏中复用的轻量基础框架，而非某个游戏的业务框架：

1. 保持模块化 UPM 包结构和清晰的 asmdef 依赖。
2. 让游戏业务逻辑、场景、UI、资源和持久化相互解耦。
3. 提供可预测的初始化、异步操作、生命周期、错误和资源所有权语义。
4. 优先使用 Unity/.NET 原生能力，不要求新的强制性第三方运行时依赖。
5. 提供最小可运行示例、自动化测试及 Windows Player 验证路径。

### 1.2 非目标

- 不内置游戏角色、背包、关卡、战斗等具体玩法系统。
- 不建设大型 DI 容器、完整 ECS 或另一套 Unity 引擎包装层。
- 不在基础 Persistence 中引入游戏 SaveService、槽位、云存档或版本迁移。
- 不强制所有项目安装新 Input System 或其他可选扩展包。
- 不在当前基础版本集成 Steamworks、FMOD/Wwise 等外部平台系统。

## 2. 现有基础与整体依赖

依据此前仓库审核记录，现有包主要包含 `Yato.Core`、`Yato.Mvc`、`Yato.Resources`、`Yato.Resources.Abstractions`、`Yato.UI`、`Yato.Editor`；具备 RuntimeHost、事件总线、ServiceLocator、资源句柄和 UI 路由等基础能力。具体代码状态应在实施前重新以仓库最新提交核对。

推荐的依赖方向如下（箭头表示“依赖于”）：

| 模块 | 直接依赖 | 设计约束 |
|---|---|---|
| `Yato.Core` | 尽量仅依赖 Unity/.NET 基础运行时 | 不引用业务模块 |
| `Yato.App` | Core；场景资源集成可通过小接口 | 负责 Bootstrap 和场景服务；不直接依赖 UI 展示 |
| `Yato.Resources` | Core、Resources.Abstractions | 资源句柄与生命周期统一管理 |
| `Yato.UI` | Core、Resources | 不直接依赖具体游戏场景和存档模型 |
| `Yato.Persistence` | Core 或最小独立程序集 | 仅文件读写、路径、序列化 |
| `Yato.Settings`（建议） | Core、Persistence | 不直接引用 Audio/Input/UI |
| `Yato.Audio`（建议） | Core、Resources | 与 Settings 通过监听/适配器协作 |
| `Yato.Input`（建议） | Core、可选 Unity Input System | 不封装重复的底层按键系统 |
| `Yato.Editor` | 运行时模块的 Editor 扩展 | 不进入 Player 构建 |

跨模块通信遵循两条规则：业务依赖由调用方注入或通过服务获取；状态变化通过可解除订阅的事件通知。资源与场景所有权必须是显式的，不靠当前 Active Scene 推断。

## 3. 已冻结设计 D01–D04

### 3.1 D01：平台优先级

**冻结：方案 A。**

- 首要运行与验收平台为 Windows Player。
- 其他平台保留可扩展性，但不对当前版本承诺全部功能一致。
- Steamworks 等平台接入以后采用独立可选适配层。

### 3.2 D02：初始化与生命周期

**冻结：总方案 B；D02-B01=B；D02-B02=A。**

- 正式构建从专用 Bootstrap Scene 启动，`GameBootstrap` 负责全局服务的注册、初始化及关闭。
- Editor 允许从任意场景直接 Play；使用 Editor-only 补初始化机制，复用相同配置和启动路径。
- Bootstrap 对象使用 `DontDestroyOnLoad`，跨场景保留；场景局部服务与全局服务的生命周期分离。
- 与已有 `RuntimeHost` 协作，但初始化必须幂等，不允许重复注册或重复创建 DDOL 根节点。
- 支持关闭 Domain Reload 的 Editor 模式；恢复静态状态并避免残留订阅。
- 服务初始化按明确顺序，失败时尽力清理已完成的初始化，关闭时按反向顺序释放。
- 全局 UI 根节点可跨场景保留，但页面是否属于场景需要明确所有权。

### 3.3 D03：场景加载、切换与卸载

**冻结：D03-01=C、02=A、03=A、04=A、05=C、06=B；05-C01=A；06-B01=A。**

| 主题 | 冻结行为 |
|---|---|
| 加载模式 | 默认 `LoadSceneMode.Single`，显式允许 Additive |
| 切换流程 | 直接异步调用 Unity SceneManager；通过事件驱动全局 Loading UI，不依赖专门 Loading Scene |
| 并发 | Single、Additive、Unload、Ready 阶段共用一个互斥执行通道；忙时立即 `Busy`，不排队 |
| 取消 | 真正开始 Unity 加载之前可取消；开始后不承诺强制取消 Unity 加载 |
| 失败 | 区分 `Success / Busy / Canceled / InvalidScene / Failed`，记录具体失败阶段；不自动回滚 |
| 资源释放 | 基于显式 `SceneContext` 的场景资源句柄追踪；全局资源另有全局上下文 |
| 就绪判定 | 区分 `Loaded` 与 `Ready`；可选每场景一个异步初始化器，初始化完成后才 Ready |
| 超时 | 初始化器支持协作取消与可配置超时，建议默认 60 秒 |

关键边界：

- 默认不假设场景 `Loaded` 就代表所有 MonoBehaviour `Start` 已执行。
- 初始化器失败时不自动回滚到旧场景，错误提示由持久化 UI/游戏层处理。
- 如果超时后的旧初始化任务仍可能修改场景，则不能提前释放 Busy 并允许新的场景操作。
- Additive 场景拥有独立 Ready 状态和资源 Context；禁止无可用替代场景时卸载最后一个可用场景。
- 多个所有者共享资源时按实际句柄/引用计数释放；迟到的异步加载结果必须被正确清理。
- 通过原生 Unity API 或未绑定 Context 直接取得的资源，不应声称可以自动追踪。

### 3.4 D04：持久化基础设施

**冻结：D04-01=A、02=A、03=B、04=A、05=B、06=B；F01=A；F02=A。**

| 主题 | 冻结行为 |
|---|---|
| 职责 | 仅基础文件存储、路径管理和序列化；不实现游戏 SaveService |
| 根目录 | 以 `Application.persistentDataPath` 为单一应用数据根目录 |
| 路径 | 游戏提供安全相对路径；拒绝绝对路径、`..` 越界及通过链接逃逸根目录 |
| 组织 | 游戏决定 `settings/`、`saves/` 等结构与存档槽位 |
| 序列化 | 默认 UTF-8 `JsonUtility` 适配器；提供可替换 `ISerializer`，不强制引入第三方库 |
| 版本 | 游戏负责 DTO 版本号、兼容性和迁移；框架不添加统一 Envelope 或自动升级 |
| 写入安全 | 同目录唯一临时文件完整写入，再安全替换目标；不内置自动备份与恢复 |
| API | 同步和异步读写均提供，使用相同的路径及安全写入语义 |
| 并发 | 同一路径的文件操作互斥；冲突立即失败，不排队；不同路径可并发 |
| 错误 | 标准 .NET 异常为主；忙时可用清晰的 `IOException` 派生异常，不建设 Result 层 |

写入及取消的边界：

1. 写入临时文件时出错，尽力保留原文件并清理临时文件。
2. 最终提交失败时报告真实结果，**不能**静默降级为直接覆盖已有文件。
3. 临时文件写完、替换前可取消；进入提交阶段后报告实际提交结果，不能把已完成提交报告为取消。
4. 同步和异步 API 都必须遵守同一路径互斥。
5. 文件缺失或损坏时报告错误，不私自创建空存档、恢复备份或删除原数据。
6. 旧的残留临时文件不能被当作正式文件；仅在确定没有活动操作后安全清理。
7. 可靠性保证受目标文件系统影响；不对所有平台承诺绝对原子提交或掉电持久性。

## 4. 推荐默认方案 D05–D08（待整体审核）

### 4.1 D05：Yato.Settings

定位：面向游戏偏好配置的轻量状态管理层，建立在 Persistence 之上。

**推荐默认能力**：

- 游戏自行定义 `[Serializable]` 的强类型 `SettingsDto`；框架不强制规定音频、分辨率、语言等业务字段。
- 默认配置可由 DTO 或可选的 ScriptableObject 资源提供；运行时数据不能直接覆盖原始默认资源。
- 提供读取当前快照、受控修改、设置变化事件、恢复默认值、显式 `Load/Save`。
- 游戏自行选择数据相对路径；可以建议 `settings.json` 示例，但不写死目录和文件名。
- 初始化顺序：加载默认值 → 尝试加载用户文件 → 应用游戏自定义校验 → 发布当前设置。
- 文件不存在时保留默认值；文件损坏时保留原文件、明确报告，运行时可暂用默认值但不自动覆盖磁盘。
- 设置变化不默认立即写磁盘；由游戏在“应用”、退出设置界面或安全时机显式保存。
- Settings 不直接控制音频、画面、输入、UI；游戏层或可选适配器订阅变化并应用效果。

**首版不做**：设置菜单生成、复杂反射式注册中心、用户账号配置、配置版本迁移、云同步、所有业务设置的统一校验器。

**验收**：默认值、修改与通知、存取一致性、缺文件、损坏文件、重复初始化/订阅释放均有测试。

### 4.2 D06：Yato.Audio

定位：以 Unity 原生 `AudioSource / AudioMixer` 为基础的跨场景音频服务。

**推荐默认能力**：

- 默认通道 `Master / Music / SFX / UI`；`Voice` 为可扩展选项。
- 两个长期 Music AudioSource 支持播放、暂停、恢复、停止、淡入淡出和交叉渐变。
- SFX 使用可配置容量上限的 AudioSource 池；支持 2D/3D、一次性/循环、播放句柄、提前停止。
- 音效结束或明确停止后回池，重复回收必须安全；达到池上限时使用可配置拒绝策略，默认丢弃新低优先级请求。
- 通过 `Yato.Resources` 获取并持有 AudioClip 的资源句柄，音效结束释放，不造成提前卸载。
- 全局 BGM 归属 Bootstrap 全局生命周期；场景循环音效通过 SceneContext 绑定并在场景卸载时释放。
- 设置音量通过 AudioMixer 暴露参数或配置映射；Audio 不直接负责音量设置落盘。
- 通过事件或服务 API 发起播放请求，游戏业务不需要直接管理音效池。

**首版不做**：FMOD/Wwise、复杂动态音乐、音频时间轴编排、语音聊天、自动音频资源数据库。

**验收**：跨场景音乐不断、渐变完成、同音效多实例、超上限行为、停止/回池/句柄释放、场景卸载清理、音量联动。

### 4.3 D07：Yato.Input

定位：Unity Input System 的**可选薄适配层**，而非重新实现一套输入引擎。

**推荐默认能力**：

- `InputActionAsset`、ActionMap、绑定由游戏定义；框架仅提供统一启用、关闭和切换上下文的辅助服务。
- 常见输入上下文如 `Gameplay / UI` 只是示例，不硬编码为框架枚举。
- 支持运行时 Rebinding、取消操作、清理 RebindingOperation 资源，以及恢复默认绑定。
- 复用官方 `SaveBindingOverridesAsJson / LoadBindingOverridesFromJson`；文件保存交由 Settings/游戏层与 D04 完成。
- 输入订阅必须有明确持有者，场景卸载/服务关闭后不遗留回调。
- `Yato.Input` 独立 asmdef，可选依赖 Input System Package；不安装该模块的项目不应受影响。

**首版不做**：直接封装 `GetKeyDown/GetAxis`、复杂本地多人协调、平台专属震动或设备图标数据库、输入录制/回放。

**验收**：ActionMap 切换、输入关闭、UI/Gameplay 状态转换、重绑定保存与恢复、重复订阅清理、可选模块独立安装。

### 4.4 D08：工程工具、示例与发布

定位：把架构能力变成可交付、可回归验证的 UPM 包。

**默认工作**：

1. 补齐各模块 asmdef、UPM `package.json`、可选依赖说明及公共 API XML/Markdown 文档。
2. 使用 EditMode 测试验证纯逻辑与异常；PlayMode 测试验证 Bootstrap、Scene、UI、资源释放和 Audio 生命周期。
3. 提供 `BasicSample`（Bootstrap、资源、UI、场景）与 `FullSample`（Settings、Audio、Input 协作）。
4. 验证完整 UPM 安装、缺失可选包时的编译隔离、Windows Player 构建、Unity 2022.3 与 Unity 6 兼容性。
5. 维护版本变更记录；在发布前检查新增 API、破坏性变更和模块依赖方向。

## 5. 实施顺序与里程碑建议

> 以下是开发排期建议，不等同于已冻结的版本承诺；应以实际仓库代码、CI 和测试结果校准。

| 阶段 | 核心任务 | 交付判定 |
|---|---|---|
| 基线稳定 | 复核现有 Core/Resources/UI；补失败场景、回归测试 | 核心包在 Editor 与 Windows Player 可重复运行 |
| App/Scene | 落实 D02、D03 初始化和场景所有权 | Single/Additive/失败/Ready/释放集成测试通过 |
| Persistence | 落实 D04 安全路径、序列化与安全写入 | 同步/异步、Busy、取消、损坏文件测试通过 |
| Settings | 落实 D05 推荐方案 | 默认值、变化通知与保存/重载测试通过 |
| Audio | 落实 D06 推荐方案 | BGM 跨场景、池与句柄生命周期通过 |
| Input | 落实 D07 可选模块 | 无模块也可构建；重绑定与上下文测试通过 |
| 发布 | 完善样例、文档、CI、UPM 验证 | 可安装、可学习、可回归测试 |

### 5.1 已知稳定性检查线索（尚未确认属于真实缺陷）

根据较早的仓库审核记录，优先检查：

- AssetBundle/资源句柄引用计数与多持有者释放。
- UI 缓存页面的资源所有权及关闭、清理行为。
- 同页面并发打开时的去重和最终可见状态。

这些目前只是**待验证的风险线索**，不能直接写为已确认 bug。实施前应针对最新代码复查。

### 5.2 最小集成验收流程

1. Windows Player 从 Bootstrap 启动并只初始化一次。
2. 加载 Settings 默认值；首次无配置文件不报致命错误。
3. 播放 BGM 并加载主场景。
4. 通过 Single 切换场景，验证 Global BGM 继续播放、原场景资源释放。
5. Additive 加载附加场景，等待异步初始化器 Ready。
6. 同一时刻发起冲突场景请求并得到 Busy；场景加载失败时 Loading UI 能结束或显示错误。
7. 修改音量并显式保存；退出后重启，恢复设置。
8. 异步保存过程中测试同路径 Busy、取消与临时文件清理。
9. 如果安装 Input 模块，测试上下文切换及按键重绑定持久化。
10. 退出时按反向顺序清理服务、订阅、AudioSource 和资源句柄，无重复释放与悬挂回调。

## 6. 架构审核要点

正式开始实现前及每个模块交付时，统一确认：

- **依赖无环**：Settings 不反向依赖 Audio/Input；UI 不直接控制 SceneService。
- **显式所有权**：全局资源、场景资源、音频资源及 UI 页面都有明确持有者。
- **错误可追踪**：失败不被静默吞掉；不对无法可靠取消的操作伪装取消成功。
- **生命周期确定**：Editor 快速进入 Play、重复 Bootstrap、场景卸载、应用退出均有清理路径。
- **可选模块真正可选**：不安装 Input、Audio 或 Settings 时 Core/Scene/Persistence 仍能编译。
- **业务保持独立**：框架没有游戏存档、战斗状态、场景流程图或具体菜单逻辑。

## 7. 最终决策状态与下一步

| 范围 | 状态 | 后续动作 |
|---|---|---|
| D01 运行平台 | **已冻结** | 按 Windows 优先验证 |
| D02 初始化与生命周期 | **已冻结** | 实现并测试 |
| D03 场景系统 | **已冻结** | 实现并测试 |
| D04 持久化基础设施 | **已冻结** | 实现并测试 |
| D05 设置管理 | **推荐默认方案** | 整体审核后实施 |
| D06 音频管理 | **推荐默认方案** | 整体审核后实施 |
| D07 输入适配 | **推荐默认方案** | 整体审核后实施 |
| D08 工程与发布 | **推荐默认方案** | 随开发持续落实 |

建议后续审核方式：直接审核本文的模块边界与里程碑，只单独处理会影响公开接口、不可逆数据操作或产生跨模块强耦合的事项；普通实现细节采用合理默认值，并通过测试验证。

**本文为设计与规划记录，不代表对应功能已实现。** D05–D08 仍为推荐默认方案，后续可结合实际代码与测试结果调整，并据此拆分开发任务。

## 8. 参考来源

- [QFramework 仓库](https://github.com/liangxiegame/QFramework)：CoreKit、UIKit、ResKit、AudioKit 等模块化工具包。
- [Unity Open Project 1：整体架构](https://github.com/UnityTechnologies/open-project-1/wiki/Game-architecture-overview)：初始化场景、持久化管理器与事件通道。
- [Unity Open Project 1：音频系统](https://github.com/UnityTechnologies/open-project-1/wiki/Audio-system)：AudioManager、SoundEmitter 池、音频请求解耦。
- [Unity Input System：运行时重绑定](https://github.com/Unity-Technologies/InputSystem/blob/develop/Packages/com.unity.inputsystem/Documentation~/user-rebinding-runtime.md)：使用官方重绑定及 Binding Override 保存机制。
- [Unity Input System 1.6：Action Bindings](https://docs.unity.cn/Packages/com.unity.inputsystem@1.6/manual/ActionBindings.html)：官方按键覆盖、重绑定和恢复方式。
- [Unity Settings Modifier](https://github.com/muhammadIdhamMaarif/Unity-Settings-Modifier)：设置数据、运行时状态、应用器和 UI 的分层实践；其完整依赖不属于本框架要求。
- [Unity AudioMixer 文档](https://docs.unity3d.com/2022.3/Documentation/Manual/AudioMixer.html)：Unity 原生分组混音和参数管理。
