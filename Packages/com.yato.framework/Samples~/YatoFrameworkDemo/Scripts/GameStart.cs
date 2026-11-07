using System;
using System.Threading;
using PureMVC.Patterns.Facade;
using UnityEngine;
using Yato.Resources;
using Yato.UI;

namespace Yato.FrameworkDemo
{
    /// <summary>
    /// 示例入口：配置路由 → 打开页面 → 返回键弹栈。
    /// 注意：P2 提供正式资源实现后，把 NullResourceService 换成 ResourceService。
    /// </summary>
    public class GameStart : MonoBehaviour
    {
        private DemoFacade m_facade;
        private IResourceService m_resources;
        private bool m_destroyed;
        private CancellationTokenSource m_setupCancellation;

        private void Awake()
        {
            m_facade = (DemoFacade)Facade.GetInstance(() => new DemoFacade());

        }

        private void Start()
        {
            // Android/WebGL StreamingAssets are URLs; the async factory reads the manifest
            // with UnityWebRequest while desktop/Editor initialization still completes inline.
            m_setupCancellation = new CancellationTokenSource();
            var setup = CreateResources();
            setup.Completed += () =>
            {
                try
                {
                    var resources = setup.GetResult();
                    if (m_destroyed) { (resources as IDisposable)?.Dispose(); return; }
                    m_resources = resources;
                    UIRouter.Configure(m_resources, m_facade);
                    OpenExample();
                }
                catch (Exception e) { if (!m_destroyed) Debug.LogException(e); }
            };
        }

        private IAwaitable<IResourceService> CreateResources()
        {
            if (!Application.isEditor)
                return BundleResourceServiceFactory.CreateAsync(ct: m_setupCancellation.Token);
            var completed = new Yato.Resources.AwaitableCompletionSource<IResourceService>();
            completed.SetResult(ResourceServiceFactory.Create());
            return completed;
        }

        private void OpenExample()
        {
            IAwaitable<UIPageHandle> opening = UIRouter.OpenAsync(DemoPages.Example, "Hello Yato");
            opening.Completed += delegate
            {
                try
                {
                    UIPageHandle handle = opening.GetResult();
                    Debug.LogFormat("[Demo] 页面已打开：{0}", handle.PageId);
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            };
        }

        private void Update()
        {
            // Android 返回键 / ESC：逐层出栈（System 层不响应）
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                UIRouter.Back();
            }
        }

        private void OnDestroy()
        {
            m_destroyed = true;
            m_setupCancellation?.Cancel();
            m_setupCancellation?.Dispose();
            m_setupCancellation = null;
            UIRouter.CancelOpen(DemoPages.Example.PageId);
            UIRouter.CloseAll();
            (m_resources as IDisposable)?.Dispose();
            m_resources = null;
        }
    }

    /// <summary>示例门面：Mediator / Proxy 通过它注册与发通知。</summary>
    public class DemoFacade : Facade
    {
    }
}
