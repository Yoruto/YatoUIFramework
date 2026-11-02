# FullSample

打开 Scenes/YatoFull.unity 后 Play。播放生成音效、修改音量并显式 Save；热更新需要已发布的 AB 根 URL，生产默认 HTTPS，本地测试需勾选允许开关。示例仅在本场景 Awake 注册并初始化服务；关闭时退订场景事件、取消更新并释放音频。无需其他示例、原工程 GameRes、TMP 或 Input。

0.5 新增：自动加载 ready 流程、真实配置目录和中性实例池。可取还实例、停止/重启资源范围以及叠加暂停。无需 Save/Input/UI.Input，也没有游戏玩法。
