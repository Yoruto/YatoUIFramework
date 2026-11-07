# Yato Resources Hot Update 0.1

按需安装的资源热更新包，依赖 Resources.Bundle 和 Persistence。提供下载、校验、版本比较、事务快照、恢复和缓存维护。这里更新的是资源，不是 C# 业务代码。

```csharp
HotUpdateBundleCachePolicy.Install(); // Editor 工具中显式安装；Play/Player 启动时自动接入默认策略。
var result = await new HotUpdateService().RunAsync(remoteRootUrl);
```

默认策略在所有 SubsystemRegistration 重置后安装，不覆盖应用已配置的自定义策略。创建 Provider 前完成更新；已有 Provider 始终固定在自己创建时的快照。卸载本包后，Bundle 包仍可独立读取内置或显式指定的本地资源。

Package Manager 可单独导入 `HotUpdateSample`。FullSample 只演示离线模块，已经不再引用此包。
