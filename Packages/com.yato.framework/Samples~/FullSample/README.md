# FullSample

打开 Scenes/YatoFull.unity 后 Play。播放生成音效、修改音量并显式 Save。示例仅在本场景 Awake 注册并初始化服务；关闭时退订场景事件并释放音频与资源。无需 Bundle、HotUpdate、其他示例、原工程 GameRes、TMP 或 Input。

0.7 起，资源热更新 GUI 移到 `com.yato.resources.hotupdate` 的独立 `HotUpdateSample`。需要更新资源时安装该可选包并单独导入；本示例的程序集不引用 Bundle 或 HotUpdate，可在 OfflineFull 包集中运行。

0.5 新增：自动加载 ready 流程、真实配置目录和中性实例池。可取还实例、停止/重启资源范围以及叠加暂停。无需 Save/Input/UI.Input，也没有游戏玩法。
