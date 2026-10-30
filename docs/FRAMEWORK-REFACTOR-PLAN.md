# Yato Framework 通用化改造总纲

> 版本：v1.0（2026-10-29）
> 范围：`e:/YatoUIFramework`（框架本体，下称「工作区」） + `E:/GameTest/Bang-`（使用现场，下称「Bang-」）
> 目标：把当前「从游戏里抽出来的 UI 半成品」升级为可独立交付的 Unity 通用框架，内置 UI、资源管理、事件、日志、代码生成与资源打包/热更能力。
> 本文档是执行依据：每一行改动都能对应到文件、任务编号和验收标准。

---

## 0. 阅读约定

| 记法 | 含义 |
|---|---|
| `T-P0-01` | 任务编号，`P0` 为阶段，`01` 为序号 |
| `[Keep]` | 保留，仅做小幅整理 |
| `[Modify]` | 在现有文件上改 |
| `[Move]` | 迁移到新目录/新包，逻辑基本不变 |
| `[Rewrite]` | 保留文件名或职责，内部重写 |
| `[Delete]` | 删除，不再保留 |
| `[New]` | 新增文件 |
| `DEF-xx` | 框架缺陷编号 |
| `RES-xx` | 资源层缺陷编号 |
| `文件:行` | 指当前仓库中的位置，供改造时定位 |

**非目标（本期不做）**：网络同步、战斗/玩法框架、商业化 SDK、Addressables 全量迁移、云端热更后台。资源热更只做「客户端拉取 + 校验 + 回退」最小闭环。

---

## 1. 现状基线

### 1.1 工程信息

| 项 | 工作区 | Bang- |
|---|---|---|
| Unity | 6000.5.6f1（实施期已升级；设计基线 2022.3.13f1c1） | 2022.3.62f3c1 |
| 框架代码量 | 18 个 .cs（约 900 行） | 26 个 .cs（含 Message/Logger） |
| 程序集定义 | 无 asmdef | 无 asmdef（框架部分） |
| 依赖 | PureMVC.dll、LitJson、VisualScripting、TMP、ComponentAutoBindTool | 同上 + FishNet/Astar/AkiBT 等 |
| 包管理 | 无 package.json、无版本号 | 无 |
| 文档/测试 | 根目录 2 行 README，无测试 | 无 |

### 1.2 框架文件清单（工作区）

| 文件 | 角色 | 现状问题 | 处置 |
|---|---|---|---|
| `Assets/YatoFramework/Script/UIManager.cs` | 门面：创建/注册/更新 | 场景查找、反射、写死命令、Proxy 名丢失、时序错误 | `[Rewrite]` 拆为 `UIRouter` + `UIPageFactory` + `UIPageStack` |
| `Assets/YatoFramework/Script/BaseMediator.cs` | 页面中介者 | 构造期加载预制体、`UnityEditor` 引用、反射建 View | `[Rewrite]` |
| `Assets/YatoFramework/Script/BaseProxy.cs` | 数据代理 | 无数据模型、无命名 | `[Modify]` 泛型化 |
| `Assets/YatoFramework/Script/BaseUIView.cs` | 视图基类 | 非 MonoBehaviour、构造期回调 | `[Rewrite]` 改为 `UIPageView : MonoBehaviour` |
| `Assets/YatoFramework/Script/BaseUI.cs` | 生命周期空壳 | 从未被调用 | `[Delete]` |
| `Assets/YatoFramework/Script/Singleton.cs` | 三种单例 | `SingletonFacade` 与 PureMVC 冲突；全局命名空间 | `[Modify]` 拆出 `Singleton`，删除 Facade 分支，`SingletonMonoBehaviour` 重写为 `RuntimeHost` |
| `Assets/YatoFramework/Script/UIDefine.cs` | 通知常量 | 拼写 `UIDfine`、全局命名空间、混入业务常量 | `[Rewrite]` 迁至 `UI/FrameworkNotifications.cs` |
| `Assets/YatoFramework/Script/UIFrameworkData.cs` | 数据载体 | `PageInfo/UIBody/ViewInitInfo/ExampleViewData` 混在一起，`afterCallBack` 从未使用 | `[Rewrite]` 拆为 `UIPageDef`/`UIPageContext`，示例数据移出 |
| `Assets/YatoFramework/Command/CreateUICommand.cs` | 打开 UI 命令 | 全局命名空间、框架依赖示例命令 | `[Move]` 改为 `UIRouter.Open`，命令层保留可选包装 |
| `Assets/YatoFramework/Command/ExampleValueChangeCommand.cs` | 示例命令 | 属于示例 | `[Move]` → `Samples~` |
| `Assets/YatoFramework/Example/*` | 示例 | 与核心同程序集，引用 LitJson | `[Move]` → `Samples~` |
| `Assets/Plugins/PureMVC.dll` | MVC 框架 | 二进制，无法调试/修补 | `[Keep]`（建议换源码，见 P1-06） |
| `Assets/Plugins/LitJson/*` | JSON 库 | 框架不应绑定 JSON 实现 | `[Delete]`（示例改用 Newtonsoft/自研） |
| `Assets/ComponentAutoBindTool-master/*` | 组件绑定 | 索引绑定、仅编辑器生成 | `[Move]` 合并进 Editor 包并改造 |

### 1.3 资源层现状（Bang-）

当前**同时存在三套资源系统**，职责重叠：

1. `Assets/ResourceManager.cs`：门面，按名字分发到 Resources 或 AB；
2. `Assets/ABManager.cs`：自研 AB 管理器（manifest、依赖、引用计数）；
3. `Assets/AssetBundleManager/*`：Unity 官方旧示例（静态字典、模拟模式、异步 Operation）。

调用关系混乱：`ResourceManager.LoadBundleAssetsOfType` 走 `ABManager`，`LoadBundleAssetsOfTypeAsync` 却走 `AssetBundleManager.LoadAssetAsync`，且从未调用 `AssetBundleManager.Initialize()`。

### 1.4 缺陷台账

#### 框架缺陷（DEF）

| ID | 位置 | 问题 | 影响 | 处置 |
|---|---|---|---|---|
| DEF-01 | `BaseMediator.cs:7`（工作区） | `using UnityEditor.PackageManager;` | Player 构建编译失败 | 删除 |
| DEF-02 | `Singleton.cs:46-82` | 自造 `SingletonFacade` 与 PureMVC `Facade.instance` 双轨；`Facade` 构造二次抛异常，`ReleaseInstance` 不清 `Facade.instance` | 重进 Play/域重载/单测必炸；先发通知会创建裸 Facade | 去掉，改用 `Facade.GetInstance(() => new UIManager())` |
| DEF-03 | `UIManager.cs:65-72` | Proxy 构造参数错，`ProxyName` 为 `null`/`""` | 所有 Proxy 共用一个 key，注册失败/数据串台/无法移除 | 统一 `{PageId}Proxy` |
| DEF-04 | `UIManager.cs:127-135` | 先 `SetActive(true)` 后 `RegisterProxy` | 首帧空 UI；`OnInit` 读不到数据 | 调整时序 |
| DEF-05 | `UIManager.cs:16,79-93` | `uuid` 生成后未使用；mediatorName = PageId | 同一页面无法开两份，重复打开产生孤儿对象 | PageId + InstanceId |
| DEF-06 | `UIManager.cs:107-121` | 初始化异常时队列不 `Clear()` | 每帧重试 + 刷屏 | try/finally |
| DEF-07 | `BaseMediator.cs:33-41` | 构造期同步 `Resources.Load` + `Instantiate` | 打开卡顿；Mediator 承担资源职责 | 拆出 `UIPageFactory`，异步加载 |
| DEF-08 | `UIManager.cs:54-72`、`BaseMediator.cs:108-121` | 字符串反射 + `Assembly.GetAssembly(null)` | 改名即崩；IL2CPP 裁剪后 `GetType` 返回 null | 生成 `UIPages` 表 + `typeof` |
| DEF-09 | `BaseUI.cs`、`UIManager.cs:102-140` | 多个空实现（`_UpdateAsyncCreateUI`、`_AddUIToManager`、`SetAllReady` 等） | 半成品 API 误导使用者 | 实现或删除 |
| DEF-10 | `UIManager.cs:21-29` | 框架构造函数注册业务命令（Bang- 甚至注册战斗命令） | 无法独立成包 | 业务启动时注册 |
| DEF-11 | `UIDefine.cs:5`、`CreateUICommand`、`SingletonFacade` | 全局命名空间 + 拼写错误 | 命名污染、同名类型歧义 | 全部收进命名空间 |
| DEF-12 | Bang- `BaseMediator.cs:33,75` | 框架反向依赖 `ResourceManager`、`UI.CloseBg` | 包无法脱离游戏编译 | 接口注入 + 遮罩由框架实现 |
| DEF-13 | `ComponentAutoBindTool.cs:73-80` | 索引绑定，重排即错位 | 静默绑错组件 | 名称常量 + 运行时校验 |
| DEF-14 | `EventDispatcher.cs:129-140` | `catch` 空实现 | 事件异常被吞 | 记录日志并上抛 |
| DEF-15 | `GED/EventComp/EventUtils` | 两套事件封装并存 | 维护混乱 | 合并为 `EventBus` |

#### 资源层缺陷（RES）

| ID | 位置 | 问题 | 影响 |
|---|---|---|---|
| RES-01 | `ABManager.cs:26-41` | 非编辑器下 `Init` 会调用两次 `InitAB`（`#else` 分支 + 后面的无条件调用）；`LoadAssetAsync` 未等待就取 `asset` | manifest 为 null，`LoadDependencies` NRE |
| RES-02 | `ABManager.cs:48-71` | 计数 ≤0 时重复 `assetReleaseTime.Add` | `ArgumentException` |
| RES-03 | `ABManager.cs:197` | `dependenciesMap.Add` 不判重 | 重复加载抛异常 |
| RES-04 | `ABManager.cs:101-130` | 加载失败不判空，直接 `abPool[name]` | `KeyNotFoundException` |
| RES-05 | `ABManager.cs:151-164` | `Unload(true)` | 卸掉在用资源，丢贴图/粉红材质 |
| RES-06 | `ABManager.cs:74-92` | bundle 引用 -1 被注释 | bundle 永不释放 |
| RES-07 | `ABManager.cs:167-175` | 路径写死 `AssetBundles/Windows` | Android/iOS/WebGL 全错 |
| RES-08 | `ABManager.cs:243-246` | `bestFitIndex == -1` 时访问 `[-1]` | 变体场景越界 |
| RES-09 | `ResourceManager.cs:208-215,352-365` | 同步用 `ABManager`、异步用 `AssetBundleManager` | 两套池子/两套 manifest |
| RES-10 | `ResourceManager.cs:210-215` | 编辑器分支被注释，Editor 走真 AB | 不打包不能跑 |
| RES-11 | `ResourceManager.cs:153-160` | 资源 key = 裸资产名 | 跨目录重名冲突 |
| RES-12 | UI 调用点（见 4.3） | 全部同步加载 | 打开界面卡顿 |
| RES-13 | `ResourceManager.cs:730-740` | `instanceMap.Add` 不判重 | 重复注册抛异常 |
| RES-14 | `StreamingAssets/Configs/*.json` | 只有 `name/path/bundle` | 无版本/哈希/大小/依赖，无法热更 |
| RES-15 | `ResourceManager.cs` | 无 Handle/租约 | 调用方无法正确释放，UI 关掉即泄漏 |
| RES-16 | `FilePathConfig.cs:13-47` | 平台路径硬编码，无持久化目录策略 | 热更无处落盘 |
| RES-17 | `EditorPathUtils.cs:22-23` | 扫描写死 `Assets/Development/*` | 换工程即失效 |
| RES-18 | `AssertBundleCreator.cs:7-58` | 4 个平台菜单各自硬编码 | 无法 CI，易漏平台 |
| RES-19 | `AssetBundleManager/Utility.cs:43-44` | `StandaloneLinux64 → "Android"` | 平台错乱 |
| RES-20 | `AssetBundleManager/AssetBundleManager.cs` | 旧示例静态单例，未初始化即用 | 异步加载不可用 |

---

## 2. 目标架构

### 2.1 分层与依赖规则

```
┌─────────────────────────────────────────────┐
│ Samples~（示例工程，不参与打包）             │
└───────────────▲─────────────────────────────┘
                │
┌───────────────┴──────────────┐   ┌──────────────────────┐
│ Yato.UI                      │   │ Yato.Editor          │
│ 页面/生命周期/层级/路由       │──▶│ 生成器/打包/校验/诊断 │
└───────────────▲──────────────┘   └──────────▲───────────┘
                │ 只依赖抽象                   │
┌───────────────┴──────────────┐              │
│ Yato.Resources.Abstractions  │◀─────────────┘
└───────────────▲──────────────┘
                │
┌───────────────┴──────────────┐
│ Yato.Resources（实现：Res/AB）│
└───────────────▲──────────────┘
                │
┌───────────────┴──────────────┐
│ Yato.Core（日志/事件/协程/服务）│
└──────────────────────────────┘
```

**硬性规则**

1. `Yato.Core` 不引用 `UnityEditor`、不引用任何业务类型；
2. `Yato.UI` 只引用 `Yato.Resources.Abstractions` 与 `Yato.Core`；
3. `Yato.Resources` 不引用 `Yato.UI`；
4. 业务/示例程序集单向引用框架，框架永不反向引用；
5. 所有 Editor 代码只在 `Yato.Editor` 程序集，且 `includePlatforms: ["Editor"]`。

### 2.2 包结构（UPM）

```
Packages/com.yato.framework/
├─ package.json                      # name/version/unity/dependencies
├─ Runtime/
│  ├─ Yato.Core.asmdef
│  ├─ Yato.Resources.Abstractions.asmdef
│  ├─ Yato.Resources.asmdef
│  ├─ Yato.UI.asmdef
│  ├─ Core/
│  ├─ Resources/
│  ├─ UI/
│  └─ Generated/                     # 工具生成的 .g.cs
├─ Editor/
│  └─ Yato.Editor.asmdef
├─ Tests/
│  ├─ EditMode/Yato.Tests.EditMode.asmdef
│  └─ PlayMode/Yato.Tests.PlayMode.asmdef
├─ Samples~/YatoFrameworkDemo/
├─ Documentation~/index.md
├─ CHANGELOG.md
└─ README.md
```

### 2.3 程序集依赖矩阵

| 程序集 | 允许引用 | 禁止引用 |
|---|---|---|
| `Yato.Core` | UnityEngine | 任何 Yato.*、UnityEditor |
| `Yato.Resources.Abstractions` | Yato.Core | UnityEditor |
| `Yato.Resources` | Core、Abstractions | Yato.UI、UnityEditor |
| `Yato.UI` | Core、Abstractions、（可选）Resources | UnityEditor、业务程序集 |
| `Yato.Editor` | 全部 + UnityEditor | — |

### 2.4 命名与资源规范

- 命名空间：`Yato.Core` / `Yato.Resources` / `Yato.UI` / `Yato.Editor`；
- 资源 key：全小写、`/` 分隔、无扩展名，如 `ui/bag/bagform`、`ui/common/btn_close`；
- 预制体路径：`Assets/GameRes/UI/{Module}/{Page}.prefab`，与 key 一一对应；
- Bundle 命名：按目录规则自动生成（`ui/{module}`、`scene/{name}`、`shader/common`、`config`），禁止手填；
- 文件编码 UTF-8 无 BOM，换行 LF。

---

## 3. 公共契约设计

### 3.1 Core 契约

```csharp
namespace Yato.Core
{
    public interface ILogger { void Info(string msg); void Warn(string msg); void Error(string msg, Exception e = null); }
    public interface IEventBus
    {
        IDisposable Subscribe<T>(Action<T> handler);
        void Publish<T>(T evt);
        void Clear();
    }
    public interface IServiceProvider  // 轻量服务定位，替代静态单例
    {
        void Register<T>(T service);
        T Resolve<T>();
        bool TryResolve<T>(out T service);
    }
}
```

要求：`Publish` 支持主线程投递（网络线程调用时入队，`Update` 消费）；异常必须记录，不允许空 `catch`。

### 3.2 资源契约

```csharp
namespace Yato.Resources
{
    public readonly struct AssetKey : IEquatable<AssetKey>
    {
        public readonly string Value;              // "ui/bag/bagform"
        public AssetKey(string value);
    }

    public interface IAssetHandle : IDisposable
    {
        UnityEngine.Object Asset { get; }
        AssetKey Key { get; }
        bool IsDone { get; }
        bool IsValid { get; }
        int RefCount { get; }
    }

    public interface IAssetHandle<T> : IAssetHandle where T : UnityEngine.Object
    {
        new T Asset { get; }
    }

    public interface IAwaitable
    {
        bool IsDone { get; }
        void GetResult();                          // 失败抛 ResourceException
        event Action Completed;
    }

    public interface IAwaitable<T> : IAwaitable { new T GetResult(); }

    public interface IResourceService
    {
        IAssetHandle<T> Load<T>(AssetKey key) where T : UnityEngine.Object;
        IAwaitable<IAssetHandle<T>> LoadAsync<T>(AssetKey key, CancellationToken ct = default) where T : UnityEngine.Object;
        IAwaitable<IAssetHandle<GameObject>> InstantiateAsync(AssetKey key, Transform parent, CancellationToken ct = default);
        IAwaitable PreloadAsync(IReadOnlyList<AssetKey> keys, IProgress<float> progress = null, CancellationToken ct = default);
        void Release(IAssetHandle handle);
        void ReleaseUnused();                      // 手动触发；内部也会定时触发
        ResourceStats Stats { get; }
    }

    public interface IResourceProvider
    {
        IAwaitable<UnityEngine.Object> LoadAsync(AssetKey key, Type type, CancellationToken ct);
        void Release(AssetKey key);
        void ReleaseUnused();
        ResourceStats Stats { get; }
    }
}
```

### 3.3 引用计数规则（必须按此实现）

1. `Load` 成功 → `AssetEntry.Ref++` → `BundleEntry.Ref++` → 递归对每个依赖 `Ref++`；
2. `Handle.Dispose` → 反向 `--`，且**只执行一次**（`Interlocked.Exchange` 防重）；
3. `Bundle.Ref == 0` → 进入待卸载队列，延迟 `N` 秒（默认 5s）后 `Unload(false)`；
4. 只允许在「场景切换 / 显式 `ReleaseUnused`」时调用 `Resources.UnloadUnusedAssets()`；
5. 禁止 `AssetBundle.Unload(true)`，除非整个 AB 系统 `Shutdown`；
6. 所有计数变更走同一把锁或主线程，避免加载线程竞争。

### 3.4 Manifest Schema

```json
{
  "schema": 2,
  "version": "1.0.3",
  "buildTime": "2026-10-29T10:00:00Z",
  "platform": "Android",
  "bundles": {
    "ui/bag": {
      "hash": "8f3a...",
      "crc": 123456,
      "size": 102400,
      "compression": "LZ4",
      "deps": ["ui/common", "shader/ui"],
      "assets": ["ui/bag/bagform", "ui/bag/itemcell"]
    }
  },
  "assets": {
    "ui/bag/bagform": "ui/bag",
    "ui/bag/itemcell": "ui/bag"
  }
}
```

生成时机：`BundleBuilder` 打包后读取 `AssetBundleManifest` + `BuildReport` 自动产出，写入 `StreamingAssets/AssetBundles/{Platform}/manifest.json`。

### 3.5 Provider 矩阵

| Provider | 使用场景 | 实现要点 |
|---|---|---|
| `EditorAssetProvider` | 编辑器/开发期 | `AssetDatabase.LoadAssetAtPath`，按 `AssetKey` → 路径映射；不读 AB |
| `ResourcesProvider` | 原型/降级 | `Resources.Load`，路径 = key |
| `BundleProvider` | 正式包 | manifest 索引、依赖递归、引用计数、延迟卸载、本地/远程 |
| `AddressablesProvider` | 可选扩展包 | 适配 `IResourceProvider`，不污染主包 |

运行时选择：`#if UNITY_EDITOR` → EditorProvider（可用配置强制走 Bundle）；否则按 manifest 是否存在 → BundleProvider，缺失则 ResourcesProvider 并告警。

### 3.6 UI 契约

```csharp
namespace Yato.UI
{
    public enum UILayer { Background = 0, Normal = 1, Popup = 2, Tips = 3, System = 4 }

    public sealed class UIPageDef
    {
        public string PageId;              // "BagForm"
        public Type MediatorType;          // typeof(UI.BagFormMediator)
        public AssetKey PrefabKey;         // "ui/bag/bagform"
        public UILayer Layer;              // Normal
        public bool Cacheable;             // 是否缓存实例
        public bool Singleton;             // 是否禁止重复打开
    }

    public sealed class UIPageContext
    {
        public UIPageDef Def;
        public int InstanceId;
        public object Args;
        public Transform Parent;
        public IAssetHandle<GameObject> PrefabHandle;
        public GameObject Instance;
    }

    public interface IUIPage
    {
        void OnCreate();                   // 实例化完成，只一次
        void OnOpen(object args);          // 每次打开
        void OnCover();                    // 被上层完全遮挡
        void OnReveal();                   // 重新露出
        void OnClose(bool destroy);        // 关闭；destroy=true 时随后销毁
    }

    public static class UIRouter
    {
        public static IAwaitable<UIPageHandle> OpenAsync(UIPageDef def, object args = null, UILayer? layer = null);
        public static void Close(UIPageHandle handle);          // 支持 await 完成关闭
        public static bool Back();                              // 返回键/ESC，弹栈
        public static bool IsOpen(string pageId);
        public static void CloseAll(UILayer? layer = null);
    }
}
```

**页面生命周期时序**

```
OpenAsync
  → 查缓存 / 创建 UIPageContext
  → ResourceService.LoadAsync(prefabKey)      [取消令牌绑定页面]
  → Instantiate → SetParent(layerRoot)
  → 创建 Mediator（typeof 直接 new，无反射字符串）
  → 创建 Proxy（可选）
  → 注册 Mediator/Proxy
  → View.OnCreate()
  → 栈顶变化：旧页 OnCover，新页 OnOpen(args)
  → 激活 GameObject（最后一步）
Close
  → OnClose → 出栈 → 旧页 OnReveal
  → 销毁实例 → 释放 PrefabHandle
  → 注销 Mediator/Proxy → 清理事件订阅
```

### 3.7 事件与日志契约

- 合并 `GED/EventComp/EventUtils` 为 `Yato.Core.EventBus`：
  - `IDisposable Subscribe<T>`，Dispose 即退订；
  - 支持 `PublishOnMainThread`；
  - 派发异常记录 `ILogger.Error` 并继续派发其余监听（不允许吞掉）。
- 日志分级：`Debug` 只在 `UNITY_EDITOR || YATO_VERBOSE` 编译；`Error` 必须带上下文（页面/资源 key）。

---

## 4. 文件级改造清单

### 4.1 框架本体（工作区）

| 文件 | 动作 | 具体要求 | 任务 |
|---|---|---|---|
| `Script/UIManager.cs` | `[Rewrite]` → `UI/UIRouter.cs` + `UI/UIPageFactory.cs` + `UI/UIPageStack.cs` + `UI/UIPageCache.cs` | 删除场景查找/反射/业务命令；拆成四个单责类 | T-P1-01 |
| `Script/BaseMediator.cs` | `[Rewrite]` → `UI/UIPageMediator.cs` | 不再创建 GameObject；泛型 `UIPageMediator<TView>`；生命周期转发 | T-P1-02 |
| `Script/BaseProxy.cs` | `[Modify]` → `UI/UIPageProxy.cs` | 支持 `ProxyName` 显式传入；增加 `BindModel/RefreshView` 约定 | T-P1-03 |
| `Script/BaseUIView.cs` | `[Rewrite]` → `UI/UIPageView.cs` | 继承 `MonoBehaviour`，实现 `IUIPage`；绑定在 `Awake` 完成 | T-P1-04 |
| `Script/BaseUI.cs` | `[Delete]` | 生命周期并入 `IUIPage` | T-P1-04 |
| `Script/Singleton.cs` | `[Modify]` | 保留泛型 `Singleton<T>`；删除 `SingletonFacade<T>`；`SingletonMonoBehaviour<T>` 改为 `RuntimeHost`（DontDestroyOnLoad + 主线程泵 + 协程宿主） | T-P0-05 |
| `Script/UIDefine.cs` | `[Rewrite]` → `UI/FrameworkNotifications.cs` | 只保留框架级常量（`UIToast`/`UIBack` 等），业务常量移出 | T-P1-05 |
| `Script/UIFrameworkData.cs` | `[Rewrite]` → `UI/UIPageDef.cs` + `UI/UIPageContext.cs` | 删除 `PageInfo/UIBody/ViewInitInfo/ExampleViewData` | T-P1-06 |
| `Command/CreateUICommand.cs` | `[Delete]` | 由 `UIRouter.OpenAsync` 取代；如保留命令风格，放入 `UI/Commands/OpenUICommand.cs` 并归属命名空间 | T-P1-07 |
| `Command/ExampleValueChangeCommand.cs` | `[Move]` → `Samples~` | — | T-P0-06 |
| `Example/**` | `[Move]` → `Samples~/YatoFrameworkDemo/` | 示例独立程序集，引用主包 | T-P0-06 |
| `Plugins/LitJson/**` | `[Delete]` | 示例改用 Newtonsoft（`com.unity.nuget.newtonsoft-json`）或直接删示例 JSON 逻辑 | T-P0-07 |
| `Plugins/PureMVC.dll` | `[Keep]` | 建议 P1 阶段换源码（便于调试与裁剪控制） | T-P1-08 |
| `ComponentAutoBindTool-master/**` | `[Move]` | 运行时代码进 `Runtime/UI/Bind/ComponentBinder.cs`；编辑器进 `Editor/CodeGen/` | T-P4-01 |

### 4.2 资源层（Bang-）

| 文件 | 动作 | 具体要求 | 任务 |
|---|---|---|---|
| `Assets/ResourceManager.cs` | `[Rewrite]` → `Runtime/Resources/ResourceService.cs` | 只做门面：key 解析、Provider 选择、Handle 管理；删除所有 `Resources.Load`/AB 细节 | T-P2-01 |
| `Assets/ABManager.cs` | `[Delete]` | 能力由 `BundleProvider` + `BundleLoader` + `BundleRefCounter` 重写实现 | T-P2-02 |
| `Assets/AssetBundleManager/AssetBundleManager.cs` | `[Delete]` | 旧官方示例，与自研冲突 | T-P2-03 |
| `Assets/AssetBundleManager/AssetBundleLoadOperation.cs` | `[Delete]` | 同上 | T-P2-03 |
| `Assets/AssetBundleManager/Utility.cs` | `[Delete]` | 平台名由 `RuntimePlatformResolver` 提供 | T-P2-03 |
| `Assets/AssetBundleManager/Editor/*` | `[Delete]` | 由 `Yato.Editor/Build/*` 取代 | T-P2-03 |
| `Assets/FilePathConfig.cs` | `[Rewrite]` → `Runtime/Resources/RuntimePaths.cs` | 平台路径、持久化目录、远程根地址可配置 | T-P2-04 |
| `Assets/CoroutineController.cs` | `[Move]` → `Core/RuntimeHost` | 或删除，统一用 awaitable 调度器 | T-P0-08 |
| `Assets/Editor/AssertBundleCreator.cs` | `[Rewrite]` → `Editor/Build/BundleBuilder.cs` | 参数化平台、压缩、输出目录、CI 入口 | T-P2-05 |
| `Assets/Editor/EditorPathUtils.cs` | `[Rewrite]` → `Editor/Build/ManifestBuilder.cs` | 去掉 `Assets/Development` 硬编码；按规范扫描 | T-P2-05 |
| `Assets/StreamingAssets/Configs/*.json` | `[Delete]` | 由 `manifest.json` 取代 | T-P2-06 |
| `Assets/StreamingAssets/AssetBundles/**` | `[Keep]` | 改为构建产物目录，不手工维护 | T-P2-05 |
| `Assets/LoadAssetBundle.cs` | `[Delete]` | 调试脚本 | T-P0-09 |
| `Assets/Example/ExampleAssetBundle.cs` | `[Move]` → `Samples~` | 演示新的 `IResourceService` | T-P0-09 |
| `Assets/Scripts/Character/CharacterCtrl.cs:23` | `[Modify]` | 注释掉的旧调用一并清理 | T-P3-05 |
| `Assets/Scripts/Battle/BattleViewManager.cs:252` | `[Modify]` | 改用 `InstantiateAsync` | T-P3-05 |

### 4.3 UI 调用点（必须一并改，否则新接口无人使用）

| 文件:行 | 现状 | 改为 |
|---|---|---|
| `YatoFramework/Script/BaseMediator.cs:33` | `ResourceManager.Instance.LoadPrefabSync(pageInfo.name)` | `await resourceService.LoadAsync<GameObject>(key)` |
| `UI/UILogic/Bag/BagFormView.cs:91` | `LoadPrefabSync(ItemCellPrefabName)` | `InstantiateAsync` + Cell 池 |
| `UI/UILogic/Main/MainFormView.cs:96` | 同上（CharacterCell） | 同上 |
| `UI/UILogic/BattleResult/BattleResultFormView.cs:131` | 同上（ItemCell） | 同上 |
| `UI/UILogic/TestBattle/TestBattleFormView.cs:177,189` | `LoadSpriteSync` / `LoadPrefabSync` | `LoadAsync<Sprite>` / `InstantiateAsync` |
| `UI/UILogic/TestSkillCell/TestSkillCell.cs:53` | `LoadSpriteSync("SkillIcon", icon)` | `LoadAsync<Sprite>`（图标走图集 key） |
| `UI/UILogic/ItemCell/ItemCell.cs:46` | `LoadSpriteSync(null, iconPath)` | 同上 |
| `UI/UILogic/CharacterCell/CharacterCell.cs:38` | `LoadSpriteSync(null, desc)` | 同上 |
| `Scripts/Battle/BattleViewManager.cs:252` | `LoadPrefabSync(prefabName)` | `InstantiateAsync` |
| `YatoFramework/Script/BaseMediator.cs`（Bang-）`:75` | `GetComponentInChildren<UI.CloseBg>()` | 由 `UIRoot` 统一生成遮罩 |

> 迁移策略：先让 `ResourceManager` 实现 `IResourceService`（内部仍用旧逻辑），把所有调用点改成接口；再替换内部实现为 `BundleProvider`。这样 UI 只改一次。

### 4.4 新增文件（完整清单）

```
Runtime/
├─ Core/
│  ├─ ServiceLocator.cs            # 轻量容器
│  ├─ ILogger.cs / UnityLogger.cs
│  ├─ MainThreadDispatcher.cs      # 主线程泵
│  ├─ RuntimeHost.cs               # DontDestroyOnLoad 宿主（协程/Update）
│  ├─ EventBus.cs                  # 类型化事件
│  ├─ AsyncOperationExtensions.cs  # awaitable 适配
│  └─ DisposableBag.cs
├─ Resources/
│  ├─ Abstractions/
│  │  ├─ AssetKey.cs
│  │  ├─ IAssetHandle.cs
│  │  ├─ IResourceService.cs
│  │  ├─ IResourceProvider.cs
│  │  ├─ IAwaitable.cs
│  │  ├─ ResourceException.cs
│  │  └─ ResourceStats.cs
│  ├─ ResourceService.cs           # 门面实现
│  ├─ AssetHandle.cs
│  ├─ AssetEntry.cs / BundleEntry.cs
│  ├─ ResourceManifest.cs          # manifest 解析与索引
│  ├─ Providers/
│  │  ├─ EditorAssetProvider.cs
│  │  ├─ ResourcesProvider.cs
│  │  ├─ BundleProvider.cs
│  │  ├─ BundleLoader.cs           # 单 bundle 异步加载/依赖
│  │  ├─ BundleRefCounter.cs
│  │  └─ RemoteDownloader.cs
│  ├─ RuntimePaths.cs
│  └─ Diagnostics/
│     ├─ ResourceTracker.cs        # 句柄堆栈追踪
│     └─ ResourceStatsCollector.cs
├─ UI/
│  ├─ UIRoot.cs                    # Canvas/层级/相机/安全区
│  ├─ UILayer.cs / UILayerRoot.cs
│  ├─ UIPageDef.cs / UIPageContext.cs / UIPageHandle.cs
│  ├─ IUIPage.cs
│  ├─ UIPageView.cs                # MonoBehaviour
│  ├─ UIPageMediator.cs
│  ├─ UIPageProxy.cs
│  ├─ UIPageFactory.cs
│  ├─ UIPageStack.cs
│  ├─ UIPageCache.cs               # LRU
│  ├─ UIRouter.cs
│  ├─ UIMask.cs                    # 点击遮罩（替代 CloseBg）
│  ├─ Bind/ComponentBinder.cs
│  └─ FrameworkNotifications.cs
└─ Generated/
   ├─ UIPages.g.cs                 # 页面表（工具生成）
   └─ AssetKeys.g.cs               # 资源 key 常量（工具生成）

Editor/
├─ Build/
│  ├─ BundleBuilder.cs             # 参数化打包 + CI 入口
│  ├─ ManifestBuilder.cs           # manifest 生成
│  ├─ BuildReportAnalyzer.cs       # 冗余依赖分析
│  └─ PlatformResolver.cs
├─ CodeGen/
│  ├─ PageCodeGenerator.cs         # Mediator/View/Proxy/Bind 生成
│  ├─ AssetKeyGenerator.cs
│  └─ CodeGenSettings.cs
├─ Tools/
│  ├─ ResourceWindow.cs            # 常驻包/引用计数/耗时
│  ├─ BindingValidator.cs          # prefab 绑定校验
│  └─ LeakReportWindow.cs
└─ Yato.Editor.asmdef

Tests/
├─ EditMode/
│  ├─ ManifestParseTests.cs
│  ├─ AssetKeyTests.cs
│  └─ RefCountTests.cs
└─ PlayMode/
   ├─ LoadReleaseTests.cs
   ├─ UIPageLifecycleTests.cs
   └─ LeakTests.cs
```

### 4.5 配置与工程文件

| 文件 | 动作 | 说明 |
|---|---|---|
| `Packages/manifest.json` | `[Modify]` | 增加 `com.unity.nuget.newtonsoft-json`（若需要）；移除不必要的 `com.unity.visualscripting` 依赖（框架侧） |
| 新增 `package.json` | `[New]` | `com.yato.framework`，版本 `0.1.0` |
| 新增各目录 `asmdef` | `[New]` | 见 2.3 依赖矩阵 |
| `ProjectSettings/*` | `[Keep]` | 不提交个人设置 |
| `.gitignore` | `[New]` | 忽略 `Library/`、`Temp/`、`Logs/`、`StreamingAssets/AssetBundles/`（构建产物） |

---

## 5. 分阶段实施计划

> 工期为单人估算，单位为「人日」，含自测，不含跨团队联调。

### P0 止血与拆包（4 人日）

| 任务 | 内容 | 涉及文件 | 验收 |
|---|---|---|---|
| T-P0-01 | 建 `Runtime/Editor/Tests/Samples~` 目录与 asmdef 骨架 | 新目录 | Unity 编译通过，空包可被引用 |
| T-P0-02 | 删 `UnityEditor`/VisualScripting 依赖 | `BaseMediator.cs`、`UIManager.cs`、`Singleton.cs` | Android/Windows Player 构建通过 |
| T-P0-03 | 修 Proxy 命名与创建时序 | `UIManager.cs` | 两个页面同时打开，Proxy 不串台；关闭后 `RetrieveProxy` 为空 |
| T-P0-04 | 修初始化队列异常不清理；重复打开检测 | `UIManager.cs` | 构造异常后不刷屏；重复打开返回已有实例或明确报错 |
| T-P0-05 | 删除 `SingletonFacade`，改 `Facade.GetInstance` | `Singleton.cs`、`UIManager.cs` | 连续 `Release + Create` 两次不抛异常 |
| T-P0-06 | 示例与命令移入 `Samples~` | `Example/**`、`Command/**` | 核心程序集不含示例类型 |
| T-P0-07 | 删除 LitJson 依赖 | `Plugins/LitJson`、示例 | 示例仍可运行 |
| T-P0-08 | 引入 `RuntimeHost` 统一 Update/协程 | 新文件、`CoroutineController.cs` | 不再需要业务手写 `UIManager.Instance.Update` |
| T-P0-09 | 删除调试脚本与旧示例资源加载 | `LoadAssetBundle.cs`、`ExampleAssetBundle.cs` | 编译无引用 |

**里程碑 M1**：框架可独立编译打包，无业务依赖，旧功能不回退。

### P1 契约与 UI 解耦（6 人日）

| 任务 | 内容 | 验收 |
|---|---|---|
| T-P1-01 | 实现 `UIRouter`（Open/Close/Back/CloseAll） | 示例可打开/关闭/返回 |
| T-P1-02 | 泛型 `UIPageMediator<TView>` | 不再需要强制类型转换 |
| T-P1-03 | `UIPageProxy` 命名与生命周期 | Proxy 与页面同生共死 |
| T-P1-04 | `UIPageView : MonoBehaviour` + `IUIPage` 生命周期 | 打开/覆盖/恢复/关闭回调顺序正确 |
| T-P1-05 | 框架通知常量收敛 | 业务常量不再出现在框架 |
| T-P1-06 | `UIPageDef` / `UIPageContext` | 支持传参、层级、缓存标记 |
| T-P1-07 | 删除 `CreateUICommand` 路径 | 无字符串命令名 |
| T-P1-08 | （可选）PureMVC 源码化 | 可断点、可裁剪 |
| T-P1-09 | 定义 `IResourceService` 抽象 + `NullResourceService` | UI 编译不依赖资源实现 |

**里程碑 M2**：UI 层可独立编译，资源由接口注入。

### P2 资源实现（10 人日）

| 任务 | 内容 | 验收 |
|---|---|---|
| T-P2-01 | `ResourceService` + `AssetHandle` + `AssetKey` 解析 | 单元测试覆盖 key 解析/句柄释放 |
| T-P2-02 | `EditorAssetProvider` | 编辑器零打包打开全部页面 |
| T-P2-03 | `ResourcesProvider`（降级） | 无 manifest 时可用并告警 |
| T-P2-04 | `BundleProvider` + `BundleLoader` + 依赖递归 | 正式包可加载全部资源 |
| T-P2-05 | `BundleRefCounter` + 延迟卸载 | 引用归零后 bundle 确实卸载（日志/面板可见） |
| T-P2-06 | `ManifestBuilder` + `BundleBuilder` | 一条命令产出 manifest + bundles |
| T-P2-07 | 删除 `ABManager` / `AssetBundleManager` / 旧配置 | 全工程无残留引用 |
| T-P2-08 | UI 调用点全部改为接口（见 4.3） | 无 `LoadPrefabSync` 调用 |
| T-P2-09 | `RuntimePaths` 平台/远程/持久化目录 | 四平台路径正确（Windows/Android/iOS/WebGL） |

**里程碑 M3**：背包/主界面/战斗界面在编辑器与真机均可用；开关 100 次无泄漏。

### P3 异步体验与加载（6 人日）

| 任务 | 内容 | 验收 |
|---|---|---|
| T-P3-01 | 异步打开页面 + 取消 | 打开中关闭，不再回调、不产生孤儿对象 |
| T-P3-02 | 加载遮罩/进度 | 首次打开大页面有 Loading |
| T-P3-03 | 预加载 | 主界面进入后预热，二次打开 < 50ms |
| T-P3-04 | 页面缓存与 LRU | 缓存命中不重新实例化；上限可配 |
| T-P3-05 | UI 列表 Cell 池 | 刷新不产生 Instantiate/Destroy 峰值 |
| T-P3-06 | `UIMask` 替代 `CloseBg` | 点击遮罩关闭由框架统一处理 |

### P4 工具链（7 人日）

| 任务 | 内容 | 验收 |
|---|---|---|
| T-P4-01 | `ComponentBinder`：名称常量 + 运行时校验 | 重排绑定不再错位；缺组件有明确报错 |
| T-P4-02 | `PageCodeGenerator`：一键生成 Mediator/View/Proxy/Bind | 生成物可重复生成、不覆盖手写区 |
| T-P4-03 | `AssetKeyGenerator`：扫描 prefab 生成 `AssetKeys.g.cs` 与 `UIPages.g.cs` | 页面表零字符串 |
| T-P4-04 | `BindingValidator`：prefab 命名/绑定/TMP 字体校验 | 提交前可批量校验 |
| T-P4-05 | `ResourceWindow`：常驻包/计数/耗时 | 运行时可查 |
| T-P4-06 | `LeakReportWindow`：句柄堆栈 | Play 结束输出未释放句柄 |
| T-P4-07 | CI 入口：`-executeMethod Yato.Editor.BuildPipelineEntry.Build` | 命令行可打包 |

### P5 热更（8 人日，可选但推荐）

| 任务 | 内容 | 验收 |
|---|---|---|
| T-P5-01 | 远端 `version.json` + 本地 manifest 比对 | 有更新时能算出差量列表 |
| T-P5-02 | `RemoteDownloader`：重试/断点/进度/取消 | 断网可恢复 |
| T-P5-03 | CRC 校验 + 磁盘缓存 + 旧包清理 | 校验失败回退内置版本 |
| T-P5-04 | `persistentDataPath` 优先级与 StreamingAssets 兜底 | 四平台可读 |
| T-P5-05 | 编辑器「模拟远程」开关 | 不打包即可验证流程 |

### P6 测试与交付（5 人日）

| 任务 | 内容 | 验收 |
|---|---|---|
| T-P6-01 | EditMode 单测（key/manifest/refcount） | 覆盖率 ≥ 60% |
| T-P6-02 | PlayMode 测试（加载/释放/生命周期） | CI 可跑 |
| T-P6-03 | 泄漏与性能基准（100 次开关） | 无增长趋势 |
| T-P6-04 | Samples 工程 + QuickStart | 新工程 30 分钟接入 |
| T-P6-05 | README/CHANGELOG/API 文档 | 文档与代码一致 |
| T-P6-06 | 版本发布流程 | Git tag → 打包 → 校验 |

**总计约 46 人日（不含热更为 38 人日）。**

---

## 6. 资源层专项设计

### 6.1 加载流程

```
LoadAsync(key)
 ├─ 1. key 规范化（小写、去扩展名、校验存在）
 ├─ 2. Provider 选择（Editor / Resources / Bundle）
 ├─ 3. 查 AssetEntry 缓存
 │    ├─ 命中 → Ref++ → 立即返回 Handle
 │    └─ 未命中 ↓
 ├─ 4. 查 BundleEntry
 │    ├─ 未加载 → BundleLoader.LoadAsync(bundle)
 │    │            ├─ 依赖递归（拓扑排序，检测环）
 │    │            ├─ 本地存在 → LoadFromFileAsync
 │    │            └─ 不存在 → 远程下载 → 校验 → 落盘
 │    └─ 已加载 → Ref++
 ├─ 5. bundle.LoadAssetAsync<T>(assetName)
 ├─ 6. 创建 Handle（记录创建堆栈，仅 Editor）
 └─ 7. 返回 Handle
```

失败路径必须给出可诊断信息：`ResourceException(key, bundle, stage, inner)`。

### 6.2 卸载策略

| 场景 | 行为 |
|---|---|
| Handle 释放 | 计数递减，bundle 归零入延迟队列 |
| 延迟队列到期 | `Unload(false)` + 移除表项 |
| 场景切换 | `ReleaseUnused()` + `Resources.UnloadUnusedAssets()` |
| 应用退出 | `Shutdown()` 全量卸载 |
| 页面缓存淘汰 | 释放 PrefabHandle 及页面内 Handle |
| 禁止 | `Unload(true)`（除 Shutdown） |

### 6.3 编辑器模拟

- 默认 `EditorAssetProvider`：`AssetDatabase.LoadAssetAtPath`，支持 `Enter Play Mode Options`（禁用域重载）下正常工作；
- 菜单 `Yato/Resources/Force Bundle Mode`：强制走 AB，用于验证打包结果；
- 打包后自动校验：manifest 中每个 `assets` 能否被 `AssetDatabase` 找到，反之每个 UI prefab 是否在 manifest 中。

### 6.4 打包管线

1. 规则扫描：按目录/后缀决定 bundle 名（`ui/{module}`、`scene/{name}`、`shader/common`、`config`、`audio/{group}`）；
2. 冗余分析：`BuildReport` 中同一资产出现在多个 bundle → 报错并建议拆公共包；
3. 构建：`ChunkBasedCompression`（LZ4），关闭 `AppendHash`（由 manifest 管理）；
4. 产出：`manifest.json` + bundles + 哈希表；
5. 复制到 `StreamingAssets/AssetBundles/{Platform}/`；
6. CI：`-batchmode -quit -executeMethod Yato.Editor.BuildPipelineEntry.BuildBundles -buildTarget Android`。

### 6.5 热更

- 版本文件：`{remote}/version.json`，含 `appVersion/platform/manifestHash`；
- 流程：拉版本 → 比对 → 生成下载列表 → 并发下载（限速）→ CRC → 写入 `persistentDataPath/AssetBundles/{Platform}/` → 更新本地 manifest → 重载；
- 回退：校验失败或下载中断 → 使用 StreamingAssets 内置版本，并上报。

### 6.6 诊断

| 能力 | 实现 |
|---|---|
| 句柄泄漏 | Editor 下 `ResourceTracker` 记录 `StackTrace`；退出 Play 汇总 |
| 常驻包 | `ResourceStats.Bundles`（名称/引用/大小/加载时间） |
| 慢加载 | 超过阈值（如 100ms）打 Warning |
| 重复资源 | manifest 中同 key 多 bundle / 同资产多 bundle |

### 6.7 资源层 bug 修复清单（与 1.4 对应）

| ID | 修复方式 |
|---|---|
| RES-01 | 重写为 `BundleLoader`，manifest 加载必须是 `await`/协程等待完成 |
| RES-02 | 计数归零统一走 `BundleRefCounter.MarkUnused`，用 `HashSet` 防重 |
| RES-03 | 依赖表用 `Dictionary.TryAdd` |
| RES-04 | 加载失败返回 null + 抛 `ResourceException`，调用方处理 |
| RES-05 | 统一 `Unload(false)`，Shutdown 才 `true` |
| RES-06 | 资产释放时同步递减 bundle 计数 |
| RES-07 | 由 `PlatformResolver` 输出平台名 |
| RES-08 | 变体解析加边界判断 |
| RES-09 | 删除 `AssetBundleManager`，只保留一套 |
| RES-10 | EditorProvider 默认接管；强制 AB 用开关 |
| RES-11 | key 使用逻辑路径（相对资源根） |
| RES-12 | 全部改异步 |
| RES-13 | `instanceMap[key] = value` 或 `TryAdd` |
| RES-14 | 新 manifest schema |
| RES-15 | 引入 Handle |
| RES-16 | `RuntimePaths` 统一平台/持久化目录 |
| RES-17 | 资源根目录可配置 |
| RES-18 | 单入口参数化构建 |
| RES-19 | 平台映射表修正 |
| RES-20 | 删除旧示例 |

---

## 7. UI 层专项

### 7.1 层级与栈

```
UIRoot (DontDestroyOnLoad)
├─ Background  (Canvas sortingOrder 0)
├─ Normal      (100)
├─ Popup       (200)   ← 弹窗默认层
├─ Tips        (300)   ← Toast/飘字
└─ System      (400)   ← Loading/断线重连
```

- 每层独立 Canvas（避免整层重建），`CanvasScaler` 由 `UIRoot` 统一配置；
- 栈按层维护：`Push/Pop/Back`；
- 弹窗打开时对下层发 `OnCover`，关闭发 `OnReveal`；
- 遮罩由 `UIMask` 统一生成，挂在 Popup 层下方，不侵入业务预制体。

### 7.2 生命周期定义

| 回调 | 时机 | 典型用途 |
|---|---|---|
| `OnCreate` | 实例化后一次 | 取组件、绑事件 |
| `OnOpen(args)` | 每次显示 | 填数据、播动画 |
| `OnCover` | 被上层遮住 | 停计时器、暂停输入 |
| `OnReveal` | 重新露出 | 刷新数据、恢复输入 |
| `OnClose(destroy)` | 关闭 | 退订事件、保存草稿 |

### 7.3 代码生成

生成 `UIPages.g.cs`：

```csharp
public static class UIPages
{
    public static readonly UIPageDef BagForm = new UIPageDef(
        pageId: "BagForm",
        mediatorType: typeof(UI.BagFormMediator),
        prefabKey: new AssetKey("ui/bag/bagform"),
        layer: UILayer.Normal,
        cacheable: false,
        singleton: true);
}
```

业务调用：

```csharp
var page = await UIRouter.OpenAsync(UIPages.BagForm, new BagArgs { Tab = 1 });
UIRouter.Close(page);
```

生成器约束：

- 只读 prefab 命名与目录，不读场景；
- 生成文件带 `// <auto-generated>` 头，禁止手改；
- 手写区用 `partial` + `#region 手写`；
- 生成前做差异比对，无变化不写盘（避免无意义 diff）。

### 7.4 绑定规范

- `ComponentBinder` 用字典：`Dictionary<string, Component>`，key 为节点路径或命名规则；
- 生成代码：

```csharp
m_Btn_Close = binder.Get<Button>("Btn_Close");
```

- 运行时校验：数量不一致、类型不匹配、空引用 → 明确报错并指出 prefab 名。

---

## 8. 测试计划

### 8.1 EditMode

| 用例 | 断言 |
|---|---|
| `AssetKey` 规范化 | 大小写/扩展名/斜杠统一 |
| manifest 解析 | 依赖、assets 索引正确；脏数据报错 |
| 引用计数 | +1/-1/重复释放/释放未加载 key |
| 依赖拓扑 | 循环依赖被检测并报错 |

### 8.2 PlayMode

| 用例 | 断言 |
|---|---|
| 加载/释放 100 次 | 常驻 bundle 数回落到基线 |
| 打开中取消 | 无回调、无实例、无句柄泄漏 |
| 页面生命周期 | 顺序为 Create→Open→Cover→Reveal→Close |
| 遮罩点击 | 只关闭最上层弹窗 |
| 返回键 | 逐层出栈，System 层不响应 |

### 8.3 性能基线

| 指标 | 目标 |
|---|---|
| 二次打开缓存页面 | < 50ms |
| 首次打开普通页面 | < 200ms（真机） |
| 列表 100 项刷新 | 无 Instantiate 峰值（池化后） |
| 内存 | 关闭全部页面后 AB 常驻为 0（除常驻包） |

---

## 9. 兼容与迁移策略

1. **分支**：`feature/framework-v1`，不改 `main`；
2. **适配器过渡**：第一阶段让旧 `ResourceManager` 实现 `IResourceService`，UI 只改接口调用；确认稳定后再替换内部实现；
3. **双跑验证**：`EditorAssetProvider` 与 `BundleProvider` 可用开关切换，同场景对比；
4. **特性开关**：`YATO_USE_BUNDLE`、`YATO_UI_ASYNC` 编译符号，便于快速回退；
5. **数据兼容**：资源 key 变更需提供映射表，旧存档中的资源名一次性迁移；
6. **提交粒度**：一个任务一个 commit，commit message 带任务号（如 `T-P2-04`）。

---

## 10. 风险登记

| 风险 | 概率 | 影响 | 应对 |
|---|---|---|---|
| `Unload` 导致在用资源丢失 | 中 | 高 | 禁用 `Unload(true)`；句柄追踪；灰度验证 |
| IL2CPP 裁剪掉生成类型 | 中 | 高 | 页面表持有 `typeof`；`link.xml` 兜底 |
| 资源 key 重命名导致旧引用失效 | 中 | 中 | 生成常量 + 编译期报错；提供映射迁移 |
| 团队仍直接调用 `Resources.Load` | 高 | 中 | CI 静态检查（禁止 API 列表）+ Code Review |
| Editor 模拟与真机行为差异 | 中 | 中 | 打包后自动冒烟；真机每日构建 |
| 旧 AB 产物与新 manifest 混用 | 中 | 高 | 产物目录版本隔离；启动校验 schema 版本 |
| 工期被业务需求打断 | 高 | 中 | 按里程碑交付，每阶段可独立使用 |

---

## 11. 完成定义（DoD）

- [ ] 框架程序集不含任何业务类型、`UnityEditor`、项目专属路径；
- [ ] 编辑器零打包可运行全部页面；
- [ ] 正式包所有资源经 manifest 寻址，无运行时目录扫描；
- [ ] 任意页面开关 100 次，句柄泄漏为 0、常驻 bundle 回落到基线；
- [ ] 打包一条命令完成，CI 与本地同一入口，产物带版本与哈希；
- [ ] 外部工程 import 后 30 分钟内跑通示例；
- [ ] README/QuickStart/CHANGELOG/API 文档齐全；
- [ ] EditMode + PlayMode 测试在 CI 全绿。

---

## 12. 里程碑

| 里程碑 | 内容 | 判定 |
|---|---|---|
| M1 | P0 完成 | 框架可独立编译打包，旧功能不回退 |
| M2 | P1 完成 | UI 只依赖接口，资源实现可替换 |
| M3 | P2 完成 | 编辑器/真机双端可跑，无泄漏 |
| M4 | P4 完成 | 工具链齐全，新人可自助开发页面 |
| M5 | P6 完成 | 可作为 v0.1 对外试用 |

---

## 附录 A：改造前后调用对照

**打开页面**

```csharp
// 改造前
PageInfo pageInfo = new PageInfo { name = "BagForm", module = "UI" };
UIManager.Instance.SendNotification("CreateUICommand", new UIBody(pageInfo));

// 改造后
await UIRouter.OpenAsync(UIPages.BagForm, args);
```

**加载资源**

```csharp
// 改造前
var prefab = ResourceManager.Instance.LoadPrefabSync("ItemCell");
var go = Object.Instantiate(prefab, content);

// 改造后
var handle = await resources.LoadAsync<GameObject>(UIKeys.ItemCell);
var go = Object.Instantiate(handle.Asset, content);
// 关闭时
handle.Dispose();
```

**关闭页面**

```csharp
// 改造前（游戏层命令，且删 Proxy 用字符串替换）
UIManager.Instance.SendNotification(UINotificationType.CloseUI, "BagForm");

// 改造后
UIRouter.Close(pageHandle);
```

**订阅事件**

```csharp
// 改造前
GED.ED.addListener(id, OnEvent);   // 需手动 remove，异常被吞

// 改造后
_bagSub = eventBus.Subscribe<BagChanged>(OnBagChanged); // Dispose 即退订
```

---

## 附录 B：目录迁移对照

| 旧位置 | 新位置 |
|---|---|
| `Assets/YatoFramework/Script/` | `Runtime/UI/` + `Runtime/Core/` |
| `Assets/YatoFramework/Command/` | 删除 / `Runtime/UI/Commands/` |
| `Assets/YatoFramework/Example/` | `Samples~/YatoFrameworkDemo/` |
| `Assets/ResourceManager.cs` | `Runtime/Resources/ResourceService.cs` |
| `Assets/ABManager.cs` | 删除（拆入 `Providers/`） |
| `Assets/AssetBundleManager/` | 删除 |
| `Assets/FilePathConfig.cs` | `Runtime/Resources/RuntimePaths.cs` |
| `Assets/CoroutineController.cs` | `Runtime/Core/RuntimeHost.cs` |
| `Assets/Editor/AssertBundleCreator.cs` | `Editor/Build/BundleBuilder.cs` |
| `Assets/Editor/EditorPathUtils.cs` | `Editor/Build/ManifestBuilder.cs` |
| `Assets/StreamingAssets/Configs/` | 删除（并入 manifest） |

---

## 附录 C：术语

| 术语 | 含义 |
|---|---|
| AssetKey | 逻辑资源路径，全小写、斜杠分隔、无扩展名 |
| Handle | 资源租约，唯一释放凭据 |
| Provider | 资源加载后端（Editor/Resources/Bundle/Addressables） |
| Manifest | 资源清单，描述 bundle、依赖、哈希、大小 |
| BundleEntry | 运行时 bundle 记录（引用计数、加载状态） |
| UIPageDef | 页面静态定义（id、mediator、prefab、层级） |
| UIPageContext | 页面运行实例（实例 id、参数、句柄） |
| UILayer | UI 层级（Background/Normal/Popup/Tips/System） |

---

## 附录 D：实施结果（2026-10-30）

### D.1 阶段完成情况

| 阶段 | 状态 | 交付物 |
|---|---|---|
| P0 止血拆包 | ✅ | `Packages/com.yato.framework` 包骨架 + 6 套 asmdef；DEF-01/02/03/04/05/06/10 修复；示例迁入 `Samples~`；LitJson / PureMVC.dll / ComponentAutoBindTool-master 清理 |
| P1 契约与 UI 解耦 | ✅ | `Yato.Core`（ILogger/EventBus/ServiceLocator/RuntimeHost/MainThreadDispatcher/DisposableBag）；`Yato.UI`（UIRoot/UIRouter/UIPageDef/Context/Handle/View/Mediator/Proxy/Factory/Stack/Cache/Mask/FrameworkNotifications）；PureMVC 源码化（`Yato.Mvc`） |
| P2 资源与打包 | ✅ | `ResourceService` + 三 Provider + `ResourceManifest`（schema v2）+ `RuntimePaths` + `BundleRefCounter`（5s 延迟卸载）；`BundleBuilder` / `ManifestBuilder` / `PlatformResolver` / `BuildPipelineEntry`；`ComponentBinder`（DEF-13） |
| P3 异步体验 | ✅ | 打开中取消、加载遮罩、预加载、LRU 缓存、`UIListPool`、遮罩关闭弹窗 |
| P4 工具链 | ✅ | `AssetKeyGenerator` / `PageCodeGenerator` / `ComponentBinderValidator` / `ResourceWindow` / `LeakReportWindow` / `ApiUsageValidator` |
| P5 热更 | ✅ | `version.json` 差量、`RemoteDownloader`（重试/进度/取消/CRC）、`HotUpdateService`（失败回退内置版本）、模拟远程窗口 |
| P6 测试与交付 | ✅ | EditMode 4 组 + PlayMode 1 组用例；`Documentation~/index.md`；Samples README 与 asmdef |

### D.2 与原始设计的偏差

| 项 | 说明 |
|---|---|
| Unity 版本 | 实施期工程升级到 6000.5.6f1；包 `unity` 字段仍为 2022.3（最低版本），代码未使用 Unity 6 专属 API |
| 生成物目录 | 由 `Runtime/Generated` 改为工程侧 `Assets/YatoGenerated`（页面表需 `typeof` 业务 Mediator，不能落在包内程序集） |
| UI 程序集依赖 | `Yato.UI` 额外引用 `Yato.Mvc`（Mediator/Proxy 基类）与 `UnityEngine.UI` |
| 公共 API | 新增 `UIRouter.Configure` / `CancelOpen` / `PreloadAsync` / `IsCached`、`ResourceServiceFactory`；核心契约严格按第 3 章，未增删 |

### D.3 已知缺口

1. `BindingValidator` 缺 TMP 字体校验（需 `Unity.TextMeshPro` 依赖，未决）；
2. 远程下载为文件级断点，无字节级 Range 续传；
3. `com.unity.textmeshpro` / `com.unity.ugui` 未升级到 Unity 6 对应版本（当前可编译）；
4. 打包、AB 模式、热更、测试均**未实跑**，仅通过编译与静态检查；
5. CI 入口未在流水线验证。

### D.4 运行验证清单

见 `Packages/com.yato.framework/Documentation~/index.md` 与仓库交流记录中的 P7 七步验证（示例 → 打包 → AB 模式 → 测试 → 100 次开关 → 热更）。
