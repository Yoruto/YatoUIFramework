# Yato Framework

Unity 通用框架包（UPM）。当前版本 `0.1.0`，处于通用化改造阶段。

## 程序集划分

| 程序集 | 目录 | 允许引用 | 说明 |
|---|---|---|---|
| `Yato.Core` | `Runtime/Core` | UnityEngine | 日志、事件、服务定位、主线程泵、运行时宿主 |
| `Yato.Resources.Abstractions` | `Runtime/Resources/Abstractions` | Core | 资源契约（AssetKey / IAssetHandle / IResourceService） |
| `Yato.Resources` | `Runtime/Resources` | Core、Abstractions | 资源实现与 Provider（Editor / Resources / Bundle） |
| `Yato.UI` | `Runtime/UI` | Core、Abstractions | 页面路由、生命周期、层级、绑定 |
| `Yato.Editor` | `Editor` | 全部 + UnityEditor + `Unity.TextMeshPro` | 打包、代码生成、诊断与绑定/字体校验（仅 Editor 平台） |
| `Yato.Tests.*` | `Tests/*` | 框架程序集 + TestRunner | EditMode / PlayMode 测试 |

## 硬性规则

1. `Yato.Core` 不引用 `UnityEditor`，不引用任何 `Yato.*` 程序集；
2. `Yato.UI` 只引用 `Yato.Resources.Abstractions` 与 `Yato.Core`，不引用资源实现；
3. `Yato.Resources` 不引用 `Yato.UI`；
4. 业务 / 示例程序集单向引用框架，框架永不反向引用；
5. 编辑器代码只存在于 `Yato.Editor`（`includePlatforms: ["Editor"]`）。

## 目录

```
Runtime/Core/                  # ILogger / IEventBus / ServiceLocator / RuntimeHost / MainThreadDispatcher
Runtime/Resources/Abstractions # 资源契约
Runtime/Resources/             # ResourceService / Providers / Manifest / RuntimePaths / Diagnostics
Runtime/UI/                    # UIRouter / UIPageFactory / UIPageStack / UIPageView / ComponentBinder
Editor/                        # Build（打包）/ CodeGen（生成器）/ Tools（诊断窗口）
Tests/                         # EditMode / PlayMode
Samples~/YatoFrameworkDemo     # 示例（不参与打包）
```

## Unity 版本

| 版本 | 状态 |
|---|---|
| 2022.3 LTS | 设计基线（`package.json` 的 `unity` 字段即最低版本） |
| 6000.5（Unity 6.5） | 已在实际工程编译通过，无 error |

Unity 6 下已知差异：

- 引擎新增 `UnityEngine.AwaitableCompletionSource<T>` 与本包 `Yato.Resources.AwaitableCompletionSource<T>` 同名，在 `Yato.Resources` 命名空间之外引用时必须写全限定名；
- 本工程已移除 `com.unity.collab-proxy`（`TreeView` 废弃）与 `com.unity.timeline 1.7.6`（`GetInstanceID` 废弃），需要 Timeline 请在 Package Manager 安装 1.8.x；
- `com.unity.textmeshpro` 与 `com.unity.ugui` 暂保持原版本（编译通过）；升级 ugui 2.0 时 TMP 会被并入，记得同步移除 TMP 包。

## 编译符号

| 符号 | 作用 |
|---|---|
| `YATO_USE_BUNDLE` | 强制走 AssetBundle（编辑器下也读 AB） |
| `YATO_UI_ASYNC` | UI 异步打开路径 |
| `YATO_VERBOSE` | 打开 Debug 级日志 |

## 状态

- [x] P0 包骨架与程序集隔离
- [x] P1 Core / UI 契约（`UIRouter` + `UIPageView`/`UIPageMediator`/`UIPageProxy`，旧 `UIManager` 已删除）
- [x] P2 资源实现与打包管线（ResourceService + 三 Provider + manifest v2 + BundleBuilder/CI + ComponentBinder）
- [x] P3 异步体验（取消打开 / 加载遮罩 / 预加载 / LRU / Cell 池 / 遮罩关闭弹窗）
- [x] P4 工具链（代码生成器 / 资源与泄漏窗口 / 禁用 API 检查 / CI 入口）
- [x] P5 热更（version.json 差量、下载重试与 CRC、失败回退、模拟远程）
- [x] P6 测试与交付（EditMode/PlayMode 用例、QuickStart、CHANGELOG、发布流程说明）
