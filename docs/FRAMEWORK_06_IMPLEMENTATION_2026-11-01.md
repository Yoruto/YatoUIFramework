# Framework 0.6 实施与验收记录

日期：2026-11-01（Asia/Shanghai）。仅 Unity 6，实测 Editor 6000.5.6f1。Yato Survivor 继续延期。

后续状态（2026-11-02）：本文保留框架阶段的历史验收范围。Survivor 首版已在后续工作中实现，原缺失构建场景也已替换为有效入口；当前状态与独立构建证据见 [Survivor 实施记录](SURVIVOR_IMPLEMENTATION_2026-11-01.md)。下文的延期、旧场景及工作区说明均描述本阶段结束时的状态。

## 实现

- TaskBridge：IAwaitable 可直接 await、AsTask，Task 可 AsAwaitable；原操作取消与资源所有权保持，后台完成投递主线程，退出时处理适配器及迟到异常。
- 完成源保留首次结果、隔离 Completed 观察者异常；RuntimeHost 提供会话 LifetimeToken，并支持销毁后重新绑定投递器。
- Core 弱引用诊断注册与只读快照；Flow / ResourceScope / Pooling / Pause / UI / 可选 Save 接入。新统一面板限频刷新。
- Core 统一资产校验接口、Project Doctor 和构建前预检；可选 UIInputProfile 复用相同规则。
- UIPageCatalog、注入资源服务的 UIStarter、ResourcesServiceHost；新项目向导只创建新目录，保留当前场景、原资产与构建列表。
- 100 轮场景/流程/UI/实例池/暂停回归，以及独立工程的 Domain/Scene Reload 矩阵驱动。

API 和使用约束见 Packages/com.yato.framework/Documentation~/foundation06.md。

## 本轮最终验收

| 验收 | 最终结果与证据 |
|---|---|
| 源工程 EditMode | 141/141 通过，零失败、零跳过；Logs/Foundation-06-Final-editor.json |
| 源工程 PlayMode | 100/100 通过，零失败、零跳过；Logs/Foundation-06-Final-playmode.json |
| 百轮生命周期 | 包含在上述 PlayMode 中；100 次实际场景创建/卸载，叠加 Flow、UI、实例池与暂停，检查租约失效及资源/诊断基线 |
| Domain/Scene Reload | 四种组合各进入/退出两次，8/8；严格捕获意外 Error/Exception/Assert，退出码 0；Logs/Foundation-06-Lifecycle-Final.log |
| 默认完整包集 | 独立工程 E:/YatoFoundation06Full，13 包且无可选包；3/3 消费侧 PlayMode 通过，包括生成 Bootstrap、真实按钮事件和资源归零；Logs/Foundation-06-Default-Tests.xml |
| 精简 UI 包集 | 独立工程 E:/YatoFoundation06Minimal，仅五个 Yato 包，最终编译退出码 0；Logs/Foundation-06-Minimal-Final.log |
| 可选包集 | 独立工程 E:/YatoFoundation06Optional，额外 Save/Input/UI.Input；验证与 Focus Mono 构建退出码 0；消费侧 1/1 通过、零跳过，Logs/Foundation-06-Optional-Tests.xml |
| Windows Mono Release | 最终构建及原生 Player 冒烟退出码均为 0，覆盖基础启动、UI、场景、Settings/Audio、Flow/Config/Pooling、本地文件热更、退出及生成的中性页面；Logs/Foundation-06-Default-Build-Final.log、Foundation-06-Windows-Player-Final.log |
| Windows 可视交互 | 使用 computer-use 技能检查诊断面板；独立 Focus Player 鼠标打开/关闭弹窗并返回主页面正常。键盘自动注入未观察到响应，原生键盘/实体手柄验收仍待完成；PlayMode 的虚拟键盘取消、虚拟手柄提交和焦点恢复测试已通过 |
| 静态检查 | 16 个包、30 个 asmdef；Yato 版本依赖、程序集引用、必要代码/资源 meta 无缺失或重复 |

消费者工程由工具创建，不复制源工程 Assets 或 ProjectSettings；源工程仍停留在原 Untitled 场景。自动化测试合计 245 项通过，八轮 Reload 验证单列；早期失败、零测试执行及旧编译产物的结果不计入通过数。

性能记录：Windows Mono Release，-batchmode -nographics，中性实例。poolPeak=8、created=8，预热后 1000 次取还 warm1000Bytes=0；60 帧 meanMs=0.374、maxMs=18.253。诊断快照允许分配；此数字不代表图形渲染或真实游戏性能。百轮资源基线回归独立于这次微基准。

复现入口：tools/Prepare-ConsumerProject.ps1；完整工程执行 Yato.ConsumerVerification.Verify / BuildWindows，PlayMode 程序集 Yato.Consumer.Tests；可选测试程序集 Yato.Optional.Consumer.Tests。生命周期执行 Yato.LifecycleVerification.Run，使用 -batchmode 且不带 -quit，由脚本完成八轮后退出并恢复设置。原生冒烟传入 --yato-smoke-root E:/YatoUIFramework/Build/AssetBundles，其下 Windows/version.json 是当前验证夹具。

## 已发现与处理

- 首轮测试中的纯 throw Task.Run 表达式重载不明确、取消断言未接受 TaskCanceledException 子类，已修正。
- UIStarter 初版依赖具体 Resources 实现，改为 IResourceServiceSource 注入，保持 UI 的抽象依赖边界。
- Unity 不允许在未保存 Untitled 场景旁创建附加场景；向导改为预制体根加独立场景资产，回归核对原场景对象、活动场景和脏状态不变。
- Unity 6.5 禁用旧 GetInstanceID；测试改为直接比较根对象，不引入新版本专属 API。
- 资源统计原先将累计加载数当成活跃数，Resources 释放闲置时又错误归零。现在服务按当前句柄表生成计数快照，Resources/Editor Provider 跟踪有效引用；不改变 Resources 不主动卸载单个共享资产的策略。新增重复句柄、不同资源、持有时 ReleaseUnused 及旧快照回归，生成页面关闭后原生 Player 统计归零。
- 裸消费者工程首次重复 Domain Reload 时触发 Unity SearchDatabase 空索引异常。准备工具为独立工程创建启用的最小搜索索引，严格驱动重跑八轮后未出现意外错误；没有屏蔽日志或修改源工程搜索设置。
- 一次消费者冒烟使用错误的热更根目录导致夹具 URL 404；纠正为 AssetBundles 父目录后最终原生运行通过。
- 源工程 Project Doctor 当前发现原有构建列表中的 Assets/Scenes/SampleScene.unity 文件缺失。保留源工程设置；独立验收工程使用明确有效的通用示例场景。

## IL2CPP 环境

当前未安装 Windows IL2CPP 模块和可用 C++/SDK 工具链。Unity CLI 安装 windows-il2cpp 重试后失败，官方发行 API 确认版本/修订号 6000.5.6f1 / 0e0577a1a2ac，但该模块官方地址 HTTP 404；Unity 中国下载地址亦为 404。未替换 Editor 版本，未计为通过。

证据：Logs/Foundation-06-IL2CPP-Install.log、Foundation-06-Modules.json、Foundation-06-Unity-Release.json。Microsoft Build Tools 安装器已从官方来源下载并验证微软签名；在所需 Unity 模块不可获取期间未安装额外工具链。

## 工作区边界

保留原 TMP 素材和项目设置改动，以及已安装 MCP 的 Packages/manifest.json / packages-lock.json 改动。MCP 是本机开发工具，不加入 com.yato.framework 的分发依赖。未新增 CI、平台 SDK 或游戏玩法。
