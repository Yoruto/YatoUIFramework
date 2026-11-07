# Yato Resources 0.4

基础资源包只依赖 `com.yato.core`。提供 `IResourceService`、异步结果、资源句柄、引用计数、Resources / Editor Provider 和诊断，不依赖 AssetBundle、UnityWebRequest 或 Persistence。

```csharp
var resources = ResourceServiceFactory.Create(new ResourcesProvider(), ownsProvider: true);
// 加载和使用资源；拥有服务的应用在退出时通过 IDisposable 释放它。
```

无参 `Create()` 在 Editor 使用 EditorAssetProvider，在 Player 使用 ResourcesProvider。AssetBundle 项目另装 `com.yato.resources.bundle`，资源热更新另装 `com.yato.resources.hotupdate`；基础工厂不会自动尝试 Bundle manifest。

从 0.3 升级时，将 Bundle 初始化改为 `BundleResourceServiceFactory`，为使用方 asmdef 添加对应可选程序集引用。公开资源类型仍使用 `Yato.Resources` 命名空间。
