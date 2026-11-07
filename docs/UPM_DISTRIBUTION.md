# UPM 模块包与离线安装（0.7.0）

支持 Unity 6（6000.0+），当前实测版本为 6000.5.6f1。框架包含 18 个独立维护版本的 Yato 包；`com.yato.framework` 是完整功能聚合包，需要精简安装时直接选择子包。

## 分发产物

执行仓库中的导出脚本：

```powershell
./tools/Export-UPMPackages.ps1
# 或指定一个尚不存在的输出目录
./tools/Export-UPMPackages.ps1 -Destination E:/YatoDelivery/0.7.0
```

默认输出到 `Build/Distribution/YatoFramework-0.7.0/` 下的新目录。输出目录已存在时拒绝覆盖。产物包括：

- `UPM/com.yato.*-<模块版本>.tgz`：18 个标准 UPM 压缩包，每个归档的包根目录为 `package/`。
- `YatoFramework-0.7.0-offline.zip`：离线嵌入式安装套件，包含 `Packages/com.yato.*`、`Install-YatoPackages.ps1`、安装说明和包集清单。
- `release-manifest.json`：来源 Git 提交、工作区状态、模块版本、依赖及校验信息。发布前应从已经提交的工作区重新导出。

导出保留 `.meta`、`link.xml`、`Samples~` 和 `Documentation~`；不包含源工程的 `Assets`、`ProjectSettings`、Unity Pipeline/MCP、本机缓存或 Survivor 游戏素材。ZIP 提供 Yato 源码及文档；Unity 的 UGUI、Input System 等外部依赖仍需从注册表安装或预先缓存。

## 推荐：从 ZIP 按需安装

1. 解压 ZIP 到独立目录。关闭目标项目的 Unity Editor，并确认已备份或提交已有工作。
2. 在目标 Unity 6 工程已经具备 `Assets`、`Packages`、`ProjectSettings` 的情况下，从解压目录执行以下命令。

```powershell
# 仅资源管理：Core + Resources，两包，不包含 Bundle、热更新、Persistence。
./Install-YatoPackages.ps1 -ProjectPath E:/MyGame -PackageSet ResourcesOnly

# 基础 UI：Core + Resources + Mvc + UI，四包。
./Install-YatoPackages.ps1 -ProjectPath E:/MyUIGame -PackageSet MinimalUI

# 仅本地 AssetBundle：Core + Resources + Resources.Bundle，三包。
./Install-YatoPackages.ps1 -ProjectPath E:/MyBundleGame -PackageSet LocalBundles

# 离线完整功能：12 包，不安装聚合包、Bundle 或热更新。
./Install-YatoPackages.ps1 -ProjectPath E:/MyOfflineGame -PackageSet OfflineFull

# 完整聚合：15 包，包含 Bundle 与资源热更新。
./Install-YatoPackages.ps1 -ProjectPath E:/MyFullGame -PackageSet Full

# 在任意包集上显式选择扩展；安装器自动补齐 Yato 传递依赖。
./Install-YatoPackages.ps1 -ProjectPath E:/MyGame -PackageSet MinimalUI -IncludeHotUpdate -IncludeInput -IncludeSave
```

上面的命令是不同工程的首次安装示例，选择一条适合当前工程的命令执行。`-WhatIf` 可以预览。安装器把选中的完整包目录放到目标 `Packages` 下，不改写目标 `manifest.json`，也不导入或改动游戏场景。目标已经包含任意 Yato 包或 Yato manifest 注册项时会拒绝继续，避免混装版本；升级或调整既有包集，请按[安装升级指南](EMBEDDED_INSTALL_AND_UPGRADE.md)整体替换需要更新的模块。

打开目标工程等待 UPM 解析依赖。使用 Input 扩展时，将 Active Input Handling 配置为 Input System 或 Both，并按 Unity 提示重启 Editor。完整聚合不默认安装 Save、Input、UI.Input，可用上述开关显式添加。

在仓库内也可以直接运行同一安装器：

```powershell
./tools/Install-YatoPackages.ps1 -ProjectPath E:/MyGame -PackageSet ResourcesOnly
```

## 单独使用 TGZ

TGZ 可通过 Unity Package Manager 的 **Install package from tarball** 安装；应先安装当前包需要的 Yato 依赖，再安装当前包。Yato 兄弟包以模块名和版本声明依赖，当前没有公开 Scoped Registry，单独安装 `com.yato.framework` 的 TGZ 或 Git 子目录 URL 不会自动取得仓库中的所有兄弟包。批量集成推荐使用 ZIP 安装器。

不要把同一模块同时装成嵌入式包和 manifest 中的 TGZ/Git/Registry 依赖。模块版本不等于整体版本，例如 Resources 是 0.4.0，Bundle/HotUpdate 是 0.1.0，Framework 聚合版本是 0.7.0。

## 使用与验证边界

- 基础入口是 `ResourceServiceFactory.Create()`，Editor 使用 `EditorAssetProvider`，Player 使用 `ResourcesProvider`。
- 本地 Bundle 项目显式使用 `BundleResourceServiceFactory.Create/CreateAsync`，相关业务 asmdef 引用 `Yato.Resources.Bundle`；类型命名空间仍为 `Yato.Resources`。
- 资源热更新需要额外安装 HotUpdate 和 Persistence，业务 asmdef 引用 `Yato.Resources.HotUpdate`。不需要资源更新时可完全省略这些包。
- 示例在各包的 `Samples~` 下，通过 Package Manager 单独导入。基础 UI 和资源框架不携带 Survivor 游戏内容；使用者应在自己的工程中构建自己的平台 AssetBundle。
- [模块消费工程与 Windows Player 验证](verification/MODULAR_RESOURCES_2026-11-07.json)和[本机 Unity 交互验证](verification/LIVE_UNITY_2026-11-07.json)记录了当前实测范围；Android、WebGL、移动设备与 IL2CPP 结果不能由 Windows Editor 结果推导。

ZIP 两包资源模式、ZIP 15 包完整模式和 TGZ 两包资源模式已在独立 Unity 工程完成导入、编译及 6 项加载/释放测试；安装器另通过 12 项包集和冲突检查，见[归档安装验证](verification/UPM_DISTRIBUTION_2026-11-07.json)。归档的 `packageSourceSha256` 和逐包 `sourceTreeSha256` 用于匹配已验证内容，归档自身 SHA-256 见各次导出的 `release-manifest.json`。

原始压缩包放在被 Git 忽略的 `Build` 目录；提交到 GitHub 的是源码、文档和可重现的导出/安装工具。
