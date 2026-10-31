# Yato Framework 快速上手

## 1. 30 分钟接入

自 `0.2.0` 起框架由多个 UPM 包组成，`com.yato.framework` 是聚合元包：

1. 把 `Packages/` 下的 `com.yato.*` 目录整体放入工程（内嵌包，Unity 自动识别，无需写 manifest 依赖）；
   只想用部分能力时，也可以只放对应子包（例如 `core` + `resources` + `ui`）；
2. 预制体放到 `Assets/GameRes/UI/{Module}/{Page}.prefab`，资源 key 规则：`ui/{module}/{page}`（全小写、无扩展名）；
3. 选中预制体 → 菜单 `Yato/CodeGen/Generate Page Code (Selection)` 生成 `View/Mediator/Proxy/Bindings`；
4. 菜单 `Yato/CodeGen/Generate Asset Keys && Pages` 生成 `Assets/YatoGenerated/{AssetKeys,UIPages}.g.cs`；
5. 注册全局服务并初始化：

```csharp
public static class GameEntry
{
    // 必须早于 Bootstrap：编辑器自动初始化 / Bootstrap Scene 都在场景加载后初始化
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        GameBootstrap.RegisterService(new MySaveService());
        GameBootstrap.RegisterService(new MySettingsService());
    }
}
```

`GameBootstrap.Initialize()` 由 Bootstrap Scene 里的组件或编辑器自动补初始化触发；重复调用幂等，失败会回滚已完成的服务并抛 `AppInitializationException`。

## 2. 页面生命周期

```
OpenAsync: 加载 → 实例化(隐藏) → 创建 Mediator/Proxy → 注册 → OnCreate
           → 入栈(旧页 OnCover) → OnOpen(args) → SetActive(true)
Close:     OnClose(destroy) → 出栈(新栈顶 OnReveal) → 注销 → 销毁 或 进 LRU
```

- `UIPageView`（MonoBehaviour）与 `UIPageMediator<TView>` 二选一或组合使用；
- Mediator 必须提供 `(string mediatorName)` 构造函数（多实例命名）；
- 关闭后若 `Cacheable && Singleton` 则进入 LRU（默认容量 8，`UIRouter.CacheCapacity`）；
- 同一页面加载中重复 `OpenAsync` 会共享同一个结果（单飞），不会产生两个实例；
- 取消只在 Unity 加载真正开始之前有效（`UIRouter.CancelOpen`）。

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

资源句柄规则：同一 key 多次加载共享一次 Provider 引用，全部 `Dispose` 后才归还；取消或类型不符时不会留下悬挂引用。

## 4. 场景与启动（D02 / D03）

```csharp
// 默认 Single；Additive 需显式指定
var request = new SceneLoadRequest("Gameplay", LoadSceneMode.Single)
    .WithInitializer(new GameplayInitializer())   // 可选：完成后才 Ready
    .WithReadyTimeout(30f);

IAwaitable<SceneLoadResult> run = GameBootstrap.Scene.LoadAsync(request);
run.Completed += () =>
{
    SceneLoadResult result = run.GetResult();
    // Success / Busy / Canceled / InvalidScene / Failed + Stage
};
```

- 场景操作共用一个互斥通道，忙时立即返回 `Busy`，不排队；
- `Loaded` 只表示 Unity 场景已加载，不保证 `Start` 已执行；`Ready` 表示初始化器完成；
- 初始化器失败不自动回滚场景；超时后先请求取消，但**旧任务真正结束前不释放通道**；
- 每个 `SceneContext` 有独立 `Resources` 范围：`context.Resources.Track(handle)` 的句柄在卸载时逆序释放；范围结束后迟到的加载结果立即回收；
- 不允许卸载最后一个可用场景。

## 5. 设置与音频（D05 / D06）

```csharp
var settings = new SettingsService<GameSettings>(
    new GameSettings(),                 // 默认值（模板，运行期不会被改写）
    new FileStore(),                    // 根目录默认 persistentDataPath
    "settings/game.json");              // 相对路径，游戏决定布局

settings.Load();                        // 默认值 → 用户文件 → 校验 → 发布
settings.Apply(s => s.MusicVolume = 0.5f);
settings.Save();                        // 显式保存；Apply 不会自动落盘

var audio = new AudioService(ResourceServiceFactory.Create(),
    new AudioServiceOptions { SfxPoolCapacity = 16 });
audio.SetBusVolume(AudioBus.Music, settings.Current.MusicVolume);

// 播放（音乐交叉渐变；SFX 返回句柄，可停止/绑定场景范围）
audio.PlayMusic(musicClip, new MusicOptions { FadeSeconds = 0.6f });
ISfxHandle sfx;
audio.TryPlaySfx(new SfxRequest(new AssetKey("audio/ui/click")) { Bus = AudioBus.Ui }, out sfx);
```

- 设置文件缺失用默认值；损坏或校验不通过时保留原文件、回退默认值并给出 `SettingsLoadResult`；
- Audio 不读写设置，音量由游戏层订阅设置变化后调用 `SetBusVolume`；
- 循环音效可用 `context.Resources.Track(sfxHandle)` 绑定到场景，卸载时自动停止。

## 6. 热更

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

## 7. 诊断

| 菜单 | 作用 |
|---|---|
| `Yato/Tools/Resource Window` | 常驻 bundle / 引用计数 / 加载耗时 |
| `Yato/Tools/Leak Report` | 退出 Play 汇总未释放句柄堆栈 |
| `Yato/Tools/Check Forbidden APIs` | 检查业务代码是否直接调用 `Resources.Load` |
| `Yato/UI/Validate Component Binders` | 预制体绑定校验（重名/空引用） |
| `Yato/HotUpdate/Settings` | 设置 / 模拟远程地址 |
| `Yato/Debug/Run Smoke Test` | 一键冒烟：自动进 Play 跑核心校验，输出 PASS/FAIL 后退出 |

## 8. 测试

- EditMode：`AssetKey` / manifest / bundle 引用计数 / 页面栈 / 页面缓存 / 资源引用计数 / 资源范围 / 文件存储 / 设置；
- PlayMode：UI 生命周期与并发、App/Scene 集成、音频池与渐变；
- 运行：Unity Test Runner（EditMode / PlayMode），或在 CI 用 `-runTests -testPlatform EditMode`；
- 包内测试要求宿主工程 `Packages/manifest.json` 含：

```json
"testables": ["com.yato.framework"]
```

## 9. 发布流程

1. 更新各包 `package.json` 版本与 `CHANGELOG.md`；
2. `git tag v0.2.0`；
3. 打包四平台 bundles 并校验 manifest；
4. 外部工程导入包并跑通示例与手动验证清单（`docs/YatoFramework_ManualVerification_2026-10-31.md`）。
