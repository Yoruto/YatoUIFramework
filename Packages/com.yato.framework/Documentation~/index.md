# Yato Framework 快速上手

## 1. 30 分钟接入

1. 把 `com.yato.framework` 放进工程的 `Packages/`（内嵌包，Unity 自动识别，无需写 manifest 依赖）。
2. 预制体放到 `Assets/GameRes/UI/{Module}/{Page}.prefab`，资源 key 规则：`ui/{module}/{page}`（全小写、无扩展名）。
3. 选中预制体 → 菜单 `Yato/CodeGen/Generate Page Code (Selection)` 生成 `View/Mediator/Proxy/Bindings`。
4. 菜单 `Yato/CodeGen/Generate Asset Keys && Pages` 生成 `Assets/YatoGenerated/{AssetKeys,UIPages}.g.cs`。
5. 启动时注入服务并打开页面：

```csharp
// 1) 门面（Mediator/Proxy 注册与通知）
var facade = (DemoFacade)Facade.GetInstance(() => new DemoFacade());

// 2) 资源服务：编辑器默认读 AssetDatabase，打包后按 manifest 读 AB
UIRouter.Configure(ResourceServiceFactory.Create(), facade);

// 3) 打开页面（异步，失败会抛 ResourceException）
IAwaitable<UIPageHandle> opening = UIRouter.OpenAsync(DemoPages.Example, "args");
opening.Completed += () => { try { var handle = opening.GetResult(); } catch (Exception e) { Debug.LogException(e); } };
```

## 2. 页面生命周期

```
OpenAsync: 加载 → 实例化(隐藏) → 创建 Mediator/Proxy → 注册 → OnCreate
           → 入栈(旧页 OnCover) → OnOpen(args) → SetActive(true)
Close:     OnClose(destroy) → 出栈(新栈顶 OnReveal) → 注销 → 销毁 或 进 LRU
```

- `UIPageView`（MonoBehaviour）与 `UIPageMediator<TView>` 二选一或组合使用；
- Mediator 必须提供 `(string mediatorName)` 构造函数（多实例命名）；
- 关闭后若 `Cacheable && Singleton` 则进入 LRU（默认容量 8，`UIRouter.CacheCapacity`）。

## 3. 资源与打包

| 场景 | Provider |
|---|---|
| 编辑器开发 | `EditorAssetProvider`（AssetDatabase，零打包） |
| 正式包 | `BundleProvider`（manifest v2 + 引用计数 + 5s 延迟卸载） |
| 无 manifest | `ResourcesProvider`（降级并告警） |

打包：菜单 `Yato/Build/Bundles (Active Platform)` 或 CI：

```
Unity.exe -batchmode -quit -projectPath <path> \
  -executeMethod Yato.Editor.BuildPipelineEntry.BuildBundles -buildTarget Android
```

产物：`Build/AssetBundles/{Platform}/manifest.json + version.json + bundles`，并同步到 `Assets/StreamingAssets/AssetBundles/{Platform}/`。

## 4. 热更

1. 打包后把 `Build/AssetBundles` 作为远端根目录（本地可用 `python -m http.server 8080`）；
2. 运行时：

```csharp
var service = new HotUpdateService();
var run = service.RunAsync(RuntimePaths.RemoteRootUrl);
run.Completed += () => {
    var result = run.GetResult();
    // 成功后重新创建 ResourceService 以加载新 manifest
};
```

失败会自动删除 `persistentDataPath` 中的 manifest，回退到 StreamingAssets 内置版本。

## 5. 诊断

| 菜单 | 作用 |
|---|---|
| `Yato/Tools/Resource Window` | 常驻 bundle / 引用计数 / 加载耗时 |
| `Yato/Tools/Leak Report` | 退出 Play 汇总未释放句柄堆栈 |
| `Yato/Tools/Check Forbidden APIs` | 检查业务代码是否直接调用 `Resources.Load` |
| `Yato/UI/Validate Component Binders` | 预制体绑定校验（重名/空引用） |
| `Yato/HotUpdate/Settings` | 设置 / 模拟远程地址 |
| `Yato/Debug/Run Smoke Test` | 一键冒烟：自动进 Play 跑 10 项核心校验，输出 PASS/FAIL 后退出 |

## 6. 测试

- EditMode：`AssetKeyTests`、`ManifestTests`（解析/依赖顺序/环检测）、`BundleRefCounterTests`、`PageStackTests`；
- PlayMode：`RouterLifecycleTests`（层级创建、无资源服务时的失败路径、取消打开、返回键）；
- 运行：Unity Test Runner（EditMode / PlayMode），或在 CI 用 `-runTests -testPlatform EditMode`。

## 7. 发布流程

1. 更新 `package.json` 版本与 `CHANGELOG.md`；
2. `git tag v0.1.0`；
3. 打包四平台 bundles 并校验 manifest；
4. 外部工程导入包并跑通示例（M5 判定）。
