# The Last Watch / Yato Survivor 实施记录

实施日期：2026-11-01；文档核对与更新：2026-11-02（Asia/Shanghai）。Framework `0.6.0`，Survivor `0.1.0`，Unity `6000.5.6f1`。首版玩法和 Windows Mono 交付已完成；以下按本地最终日志记录验证范围，不将自动模拟等同于人工完整试玩。

## 当前实现

- 专用入口：`Assets/SurvivorSample/Scenes/SurvivorBootstrap.unity`；战斗场景：`SurvivorArena.unity`。从入口 Play，使用 WASD / 方向键 / 手柄左摇杆移动，自动攻击；Escape 或右上暂停按钮打开暂停。
- 10 分钟关卡，一名骑士、4 类敌人、剑 / 弓 / 自动索敌法杖、三选一成长、金币 / 药水 / 宝箱 / 经验、死亡与胜利结算、重开 / 返回菜单。
- 8 张配置表和 ConfigCatalog；8 个 UIRouter 页面及框架生成的 View / Mediator / Proxy / ComponentBinder 绑定，实体使用 ResourceInstancePool，暂停使用 PauseService，输入使用 InputContextService / UIInputCoordinator。
- SettingsService 管理音量与显式保存、移动上方向键重绑定；SaveService 管理历史记录和显式备份恢复；AudioService 播放本示例合成的音乐与音效。
- 纯规则程序集 `Yato.Survivor.Domain` 不引用 Unity；游戏代码和 Editor 工具只在 `Assets/SurvivorSample`，未修改框架运行时与编辑器实现。
- 首版界面使用随资源一起分发的 Liberation Sans，保留 OFL；当前为英文界面。音频为本次程序合成的原始波形，不依赖外部音频素材。

## 最终实测证据

本表来自 2026-11-01 的实际日志，本次整理未重新执行百轮、完整战斗、压力或 Player 构建。可随仓库查看的[验证摘要](verification/SURVIVOR_2026-11-01.json)保留原始文件名称、SHA-256 与结果；原始 `Logs/` 和 `Build/` 不纳入 Git。2026-11-02 当前工作区另行重跑的检查见[提交前验证](verification/PRECOMMIT_2026-11-02.json)。

| 检查 | 结果 / 证据 | 范围与限制 |
|---|---|---|
| 首批美术导入 | 49 PNG / 152 Sprite，哈希与导入检查通过 | 原始素材清单位于 docs/SurvivorArtManifest.json |
| 最终规则测试 | EditMode 9 / 9 通过，零失败 / 跳过 | Logs/Survivor-Final-EditMode.json；含分段波次连续覆盖、真实配置 / SpriteSet、经验、升级与历史规则 |
| 最终路由集成 | PlayMode 1 / 1 通过，零失败 / 跳过 | Logs/Survivor-Final-PlayMode-Passed.json；覆盖真实移动更新、边界刷怪、暂停与待升级冲突、结算 / 重开和资源释放 |
| 生命周期 | 100 次开局 / 结算 / 重开通过，稳定活跃资源 key 数 3，场景泄漏 0 | Logs/Survivor-Lifecycle-Verification.json；升级与暂停同时检查。最初误用默认存档目录的数据已移入 Logs，后续使用隔离目录 |
| 强制 Bundle 生命周期 | 首轮 100 次通过，稳定活跃资源 key 数 3，场景泄漏 0 | Logs/Survivor-Bundle-Lifecycle-Verification.json；这是较早一轮记录，不称为最终全部改动后的百轮复测 |
| 最终完整战斗模拟 | 600.0378 秒胜利、2339 击杀、718 金币、38 级 | Logs/Survivor-Full-Combat.json；ForceBundles=true，加速执行真实 Session.Tick 和路由升级按钮，不修改血量 / 伤害；不是实时十分钟人工试玩 |
| Windows Mono | 构建成功，0 错误、1 警告，101,117,867 字节（约 96.4 MiB） | Logs/Survivor-Windows-Build.json；本机输出 Build/SurvivorWindows，运行目录另整理为 Build/TheLastWatch |
| 独立消费工程 | 最终编译、Bundle 与 Player 构建成功，批处理退出码 0 | Logs/Survivor-Consumer-Final-Build.log；消费工程由 tools/Prepare-SurvivorConsumer.ps1 创建，不包含本机 Pipeline 包 |
| 原生 Player 日志 | 独立构建启动、使用 BundleProvider（4 bundles / 86 assets）并正常关闭 | Logs/Survivor-Native-UI-Final.log；此日志不能单独证明鼠标点击、实体键盘 / 手柄或跨进程持久化全部通过 |
| 设置与输入专项 | 设置 / 改键往返、源 action 不变、虚拟手柄提交、键盘移动 / Escape 暂停恢复、HUD pointer、损坏主档保护与显式恢复均通过 | Logs/Survivor-Settings-Input-Verification.json；隔离数据目录及虚拟设备 |
| Project Doctor | 构建入口先执行 ValidateOrThrow，最终源工程与消费工程构建成功 | 以最终构建成功为本次证据，不单列没有独立报告的 Doctor 测试数 |

首次完整模拟的简单绕圈机器人曾在约 226 秒死亡；改善其拾取、躲避和升级选择后完成胜利。失败日志保留，不计入通过。原生 UI 首轮发现标题裁切、血条形状及 HUD 指针输入问题，已在示例代码 / 资产制作步骤调整；输入专项与最终集成通过。最终原生窗口的完整人工操作仍按下文单列。

## 压力测量

Windows Mono Release，有图形渲染，1600×900、Ultra；Intel Core i7-11800H、RTX 3060 Laptop GPU、系统内存 16,106 MB。预热后采样 60.003 秒、9,882 帧，最低保持 200 敌人 / 100 投射物 / 200 拾取物；平均帧耗时 6.072 ms、P95 6.065 ms，池溢出 0。证据为 `Logs/Survivor-Release-Pressure.json`。

夹具稳定生命和待升级队列，固定实体数量并回收额外掉落，不能作为正常难度胜利的证据。分配计数的 4,096 字节正对照失败，`MainThreadAllocatedBytes=-1`、`AllocationCounterSupported=false`，因此不宣称零分配。采样期间 Gen0 回收 4 次，托管堆从 2,822,144 增至 2,899,968 字节；这是净使用量，不是累计分配。

## 工具与复现

- `Yato/Survivor/Create or Update Sample Assets`：制作默认配置、输入、表现集合、实体 / 页面 Prefab 和场景。现有场景保留，只更新其引用的根 Prefab；不覆盖当前打开的场景文件，避免触发外部修改重载弹窗。
- `Yato/Survivor/Build Windows Game`：Project Doctor → 真实 Windows Bundles → Windows Mono Player。输出 `Build/SurvivorWindows/TheLastWatch.exe`。临时设置公司 / 产品 / 分辨率 / Mono 后恢复宿主设置。
- `tools/Verify-Survivor.cs`：在已打开的 Play 中通过 Unity run_script 执行 `VerifySurvivor.Lifecycle`、`FullCombat`、`SettingsAndInput`；检查真实游戏和页面，报告写入 Logs。
- 自动化使用环境变量 `YATO_SURVIVOR_TEST_DATA` 或独立 DataRoot；如修改入口 SurvivorGame.DataRoot，结束后恢复为空。发布构建必须使用空 DataRoot；构建入口会检查该条件。
- `tools/Prepare-SurvivorConsumer.ps1 -Destination <新目录>`：复制所需 16 个框架包、游戏代码与资源，仅复用 Unity 版本和构建列表，生成输入模式 Both 的最小 PlayerSettings。目标须位于源工程外且尚不存在；不复制 Library、本机 Pipeline 或全部原工程设置。
- 资源审计 JSON 已移至 docs，PNG 和 GUID 保持；原失效 SampleScene 构建项已换为有效示例入口与 Arena。

Unity 开发工具的命令超时不等于游戏挂起。本轮一次工具超时由“场景文件被外部修改，是否重载”弹窗造成；通过 computer-use 查看并重载本次生成的示例场景后恢复。后续制作步骤已避免覆盖已存在场景。

## 尚未验收与后续范围

- 最终 Windows 原生窗口中的完整鼠标 / 实体键盘 / 手柄操作、实际听音、退出后重新启动持久化，以及实时十分钟人工试玩。
- Windows IL2CPP 与其他平台构建；此前 IL2CPP 模块下载受阻，不计为通过。
- 可靠的累计分配量测量；本机 Mono 分配计数正对照未通过。
- 问题记录内的同叶子 key、卸载失败、迟到资源等专项故障注入，以及框架统一 API / 工具改进；这些计划不由当前正常路径测试代替。
- 最终所有改动后的强制 Bundle 百轮复测。现有百轮、最终 Bundle 完整战斗和最终路由集成分别保留其实际验证范围。

IL2CPP、实体手柄与实际听音不因模拟设备或 Mono 通过而宣称通过；框架统一修补继续留在 SURVIVOR_FRAMEWORK_ISSUES.md。
