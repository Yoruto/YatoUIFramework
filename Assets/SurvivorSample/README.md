# The Last Watch / Yato Survivor

Unity 6（本次实测 6000.5.6f1）上的完整单机幸存者示例。框架版本 0.6.0，游戏版本 0.1.0；界面为英文。

## 运行

Editor 打开 `Scenes/SurvivorBootstrap.unity` 后 Play。本机已整理的 Windows 交付在工程根目录 `Build/TheLastWatch/TheLastWatch.exe`，保留同目录的 Data、Mono 和 DLL 文件。`Build/` 不纳入 Git；首次克隆后通过 `Yato/Survivor/Build Windows Game` 构建，输出为 `Build/SurvivorWindows`。

- WASD / 方向键 / 手柄左摇杆移动，角色自动攻击。
- 收集蓝色经验宝石，升级时选择一项祝福；可解锁弓与自动索敌法杖。
- 金币计入本局和历史记录；药水回复生命；精英掉落的宝箱提供金币和一次额外祝福。
- 生存十分钟获胜，生命归零失败；结算后可重开或返回营地。
- Escape / 手柄 Start / 右上暂停按钮暂停。设置中的 Back 返回上一页；升级选择必须做出选择。
- 手柄方向键 / 左摇杆导航，A 确认，B 返回。
- 设置页音量即时生效；Save Settings 保存音量和改键。Rebind Move Up 修改 W 对应的上方向绑定，Escape 取消改键；Reset Controls 恢复默认绑定后仍需 Save。

历史记录与设置通过框架保存到 `Application.persistentDataPath` 下的 `survivor` 目录。损坏历史主档保留原文件；有可用备份时在设置页显式选择 Recover History Backup。首版不保存正在进行的战斗。

## 开发入口

- `Runtime/Bootstrap/SurvivorGame.cs`：服务注册、Flow、场景准备、路由页面与协作退出。
- `Runtime/Session/SurvivorRunSession.cs`：局内更新、武器、掉落与框架实例池租约。
- `Domain/RunProgress.cs`：纯 C# 经验、成长、随机候选和结算规则。
- `Runtime/Config`：8 类 ConfigTable；资产在 `Assets/GameRes/Config/Survivor`。
- `Runtime/Presentation/Generated`：框架生成的页面 View / Mediator / Proxy / 组件绑定及 key。手写页逻辑统一使用 View 事件 → Mediator → 应用用例，Proxy 驱动数据显示。
- `Assets/GameRes/Survivor/Presentation/SurvivorArt.asset`：Sprite 子资源、输入、音频和资源 key 集合，通过资源服务加载并持有句柄。
- `Yato/Survivor/Create or Update Sample Assets`：重建默认示例资产；会重置默认配置与页面 Prefab，现有场景文件保留。
- `Yato/Survivor/Build Windows Game`：校验、AssetBundle 和 Windows Mono 构建。

资源加载走 IResourceService；场景走 SceneService；流程走 Flow；实体走 ResourceInstancePool；暂停走 PauseService；UI 和输入走 UIRouter / InputContextService / UIInputCoordinator；持久化与音频使用 Settings / Save / Audio。框架运行时与编辑器实现未为本示例修改；提交前仅修正了热更测试对单 Bundle 产物的假设，示例输入测试也显式设置并恢复无窗口验证所需的焦点策略。

生成通用 key / 页面表时，将框架代码生成输出目录设为本示例 `Runtime/Presentation/Generated`，资源根为 `Assets/GameRes`。页面绑定文件如需随新控件刷新，删除对应的 `.Bindings.cs` 后通过框架 PageCodeGenerator 重新生成；手写 View / Mediator / Proxy 文件保留。

## 验证与移植

EditMode 程序集 `Yato.Survivor.Tests.EditMode`；PlayMode 程序集 `Yato.Survivor.Tests.PlayMode`。项目根的 `tools/Verify-Survivor.cs` 可通过 Unity run_script 执行生命周期、完整战斗和输入 / 存档专项。验证时使用独立 DataRoot 或 `YATO_SURVIVOR_TEST_DATA`，发布场景 DataRoot 保持空。

`tools/Prepare-SurvivorConsumer.ps1 -Destination <新目录>` 生成独立消费工程，复制游戏与所需框架包，不复制 Library 或本机 Pipeline 包。项目设置只复用 Unity 版本与当前构建场景列表，另行生成最小 PlayerSettings（输入模式 Both）；目标必须是源工程外尚不存在的目录。

压力夹具仅在传入 `--survivor-profile-output <完整 JSON 路径>` 时启用；同时传入 `--survivor-test-data <独立数据目录>`。它固定实体数量、稳定生命与升级队列，预热后采样 60 秒并正常退出，不能作为正常难度胜利的证据。正常启动不创建夹具。

美术使用用户提供的像素素材；原图审计见项目 `docs/SurvivorArtManifest.json`。字体保留 `SurvivorSans-LICENSE.txt`；音乐与音效由 `tools/Generate-SurvivorAudio.py` 合成。

完整实测结果和框架改进候选见[实施记录](../../docs/SURVIVOR_IMPLEMENTATION_2026-11-01.md)与[问题记录](../../docs/SURVIVOR_FRAMEWORK_ISSUES.md)。随仓库保存的[验证摘要](../../docs/verification/SURVIVOR_2026-11-01.json)来自 2026-11-01 本地日志；IL2CPP、实体手柄和实际听音仍待验收。
