# Yato Resources Bundle 0.1

可选 AssetBundle 后端，依赖 Core 和 Resources，不依赖热更新或 Persistence。提供 manifest、路径校验、引用计数、延迟卸载、跨平台 Bundle 工厂及 Editor 打包工具。

```csharp
var resources = await BundleResourceServiceFactory.CreateAsync();
// 将服务注入业务；退出时通过 IDisposable 释放。
```

该工厂在 Editor 和 Player 都明确读取 Bundle。需要在编辑器直接读 AssetDatabase 时，使用基础 `ResourceServiceFactory`。不再使用全局 `ForceBundleMode` 开关。

Android/WebGL 的内置 StreamingAssets 仍需要 UnityWebRequest；安装此包会带入相关 Unity 模块。无热更包时缓存策略仅解析本地目录，不读取热更指针，不下载、不提交更新。应用可以在创建 Provider 前设置 `BundleCachePolicy.Current`。

打包菜单为 `Yato/Build/Bundles (...)`；批处理入口仍为 `Yato.Editor.BuildPipelineEntry`。如果自定义 asmdef 直接调用这些工具，增加 `Yato.Resources.Bundle.Editor` 引用。
