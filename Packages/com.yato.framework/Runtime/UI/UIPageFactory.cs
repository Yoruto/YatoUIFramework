using System;
using System.Threading;
using UnityEngine;
using Yato.Core;
using Yato.Resources;

namespace Yato.UI
{
    /// <summary>
    /// 页面工厂：异步加载预制体 → 实例化 → 创建 View / Mediator / Proxy（DEF-07/DEF-08 的替代）。
    /// 不再使用字符串反射查找类型，MediatorType 由生成的页面表以 typeof 提供。
    /// </summary>
    public sealed class UIPageFactory
    {
        private static int s_nextInstanceId = 1;

        private readonly IResourceService m_resources;

        public UIPageFactory(IResourceService resources)
        {
            m_resources = resources ?? NullResourceService.Instance;
        }

        /// <summary>
        /// 异步创建页面实例。返回的 Context 已完成 OnCreate，但尚未显示。
        /// </summary>
        public IAwaitable<UIPageContext> CreateAsync(
            UIPageDef def,
            object args,
            UILayer layer,
            Transform parent,
            CancellationToken ct = default(CancellationToken))
        {
            // Unity 6 也有 UnityEngine.AwaitableCompletionSource<T>，此处必须全限定
            var source = new Yato.Resources.AwaitableCompletionSource<UIPageContext>();

            if (def == null || !def.IsValid)
            {
                source.SetException(new ResourceException(
                    null,
                    null,
                    ResourceStage.ProviderSelect,
                    string.Format("UIPageDef 非法：{0}", def == null ? "null" : def.PageId)));
                return source;
            }

            Transform container = parent != null ? parent : UIRoot.Instance.GetContainer(layer);

            IAwaitable<IAssetHandle<GameObject>> load;
            try
            {
                load = m_resources.LoadAsync<GameObject>(def.PrefabKey, ct);
            }
            catch (Exception e)
            {
                source.SetException(e);
                return source;
            }

            load.Completed += delegate
            {
                IAssetHandle<GameObject> prefabHandle = null;
                GameObject instance = null;
                try
                {
                    prefabHandle = load.GetResult();
                    if (prefabHandle == null || prefabHandle.Asset == null)
                    {
                        throw new ResourceException(
                            def.PrefabKey.ToString(),
                            null,
                            ResourceStage.AssetLoad,
                            "预制体加载失败");
                    }

                    instance = UnityEngine.Object.Instantiate(prefabHandle.Asset, container, false);
                    instance.name = def.PageId;
                    instance.SetActive(false);

                    UIPageContext context = BuildContext(def, args, container, prefabHandle, instance);
                    context.Layer = layer;
                    source.SetResult(context);
                }
                catch (Exception e)
                {
                    if (instance != null)
                    {
                        UnityEngine.Object.Destroy(instance);
                    }

                    if (prefabHandle != null)
                    {
                        prefabHandle.Dispose();
                    }

                    Log.Error("UIPageFactory", string.Format("创建页面失败：{0}", def.PageId), e);
                    source.SetException(e);
                }
            };

            return source;
        }

        private static UIPageContext BuildContext(
            UIPageDef def,
            object args,
            Transform parent,
            IAssetHandle<GameObject> prefabHandle,
            GameObject instance)
        {
            var context = new UIPageContext
            {
                Def = def,
                InstanceId = s_nextInstanceId++,
                Args = args,
                Parent = parent,
                PrefabHandle = prefabHandle,
                Instance = instance
            };

            UIPageView view = ResolveView(def, instance, context);
            context.View = view;

            if (def.MediatorType != null)
            {
                if (!typeof(UIPageMediator).IsAssignableFrom(def.MediatorType))
                {
                    throw new ResourceException(
                        def.PageId,
                        null,
                        ResourceStage.Instantiate,
                        string.Format("MediatorType 必须继承 UIPageMediator：{0}", def.MediatorType.FullName));
                }

                UIPageMediator mediator = CreateMediator(def.MediatorType, context.InstanceName);
                mediator.Context = context;
                mediator.View = view;
                context.Mediator = mediator;
                context.Page = mediator;

                // Proxy 由 Mediator 决定（可选）
                mediator.CreateProxyInternal();
            }
            else
            {
                context.Page = view;
            }

            if (context.Page == null)
            {
                throw new ResourceException(
                    def.PageId,
                    null,
                    ResourceStage.Instantiate,
                    "页面既没有挂 UIPageView，也没有配置 Mediator，无法驱动生命周期");
            }

            context.Page.OnCreate();
            return context;
        }

        /// <summary>优先复用预制体上的 View；缺失时按 Mediator 的泛型参数补挂。</summary>
        private static UIPageView ResolveView(UIPageDef def, GameObject instance, UIPageContext context)
        {
            Type viewType = ResolveViewType(def);
            UIPageView view = null;

            if (viewType != null && typeof(UIPageView).IsAssignableFrom(viewType))
            {
                view = instance.GetComponentInChildren(viewType, true) as UIPageView;
                if (view == null)
                {
                    view = (UIPageView)instance.AddComponent(viewType);
                }
            }

            if (view == null)
            {
                view = instance.GetComponentInChildren<UIPageView>(true);
            }

            if (view != null)
            {
                view.Context = context;
            }

            return view;
        }

        /// <summary>从 <c>UIPageMediator&lt;TView&gt;</c> 的泛型实参推断视图类型。</summary>
        private static Type ResolveViewType(UIPageDef def)
        {
            Type type = def.MediatorType;
            while (type != null && type != typeof(object))
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(UIPageMediator<>))
                {
                    return type.GetGenericArguments()[0];
                }

                type = type.BaseType;
            }

            return null;
        }

        private static UIPageMediator CreateMediator(Type mediatorType, string instanceName)
        {
            try
            {
                return (UIPageMediator)Activator.CreateInstance(mediatorType, new object[] { instanceName });
            }
            catch (MissingMethodException)
            {
                // 没有 (string) 构造：退回无参构造，多实例时会共用 Mediator 名
                Log.Warn(string.Format(
                    "UIPageFactory: {0} 缺少 (string mediatorName) 构造函数，多实例场景可能命名冲突",
                    mediatorType.FullName));
                return (UIPageMediator)Activator.CreateInstance(mediatorType);
            }
        }
    }
}
