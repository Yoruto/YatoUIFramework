using System;
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

        private void Awake()
        {
            m_facade = (DemoFacade)Facade.GetInstance(() => new DemoFacade());

            // 编辑器下默认走 AssetDatabase；打包后按 manifest 走 BundleProvider，缺失则降级 Resources
            UIRouter.Configure(ResourceServiceFactory.Create(), m_facade);
        }

        private void Start()
        {
            IAwaitable<UIPageHandle> opening = UIRouter.OpenAsync(DemoPages.Example, "Hello Yato");
            opening.Completed += delegate
            {
                try
                {
                    UIPageHandle handle = opening.GetResult();
                    Debug.LogFormat("[Demo] 页面已打开：{0}", handle.PageId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            };
        }

        private void Update()
        {
            // Android 返回键 / ESC：逐层出栈（System 层不响应）
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                UIRouter.Back();
            }
        }
    }

    /// <summary>示例门面：Mediator / Proxy 通过它注册与发通知。</summary>
    public class DemoFacade : Facade
    {
    }
}
