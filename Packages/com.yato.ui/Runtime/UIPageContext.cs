using UnityEngine;
using Yato.Resources;

namespace Yato.UI
{
    /// <summary>页面运行实例：一次打开对应一个 Context（文档 3.6）。</summary>
    public sealed class UIPageContext
    {
        public UIPageDef Def;

        /// <summary>自增实例 id；同一页面可并存多份。</summary>
        public int InstanceId;

        /// <summary>实际所在层级（可覆盖 <see cref="UIPageDef.Layer"/>）。</summary>
        public UILayer Layer = UILayer.Normal;

        /// <summary>打开参数。</summary>
        public object Args;

        /// <summary>挂载父节点（层级根）。</summary>
        public Transform Parent;

        /// <summary>
        /// 预制体句柄。缓存页在缓存期间继续持有，仅在 <c>UIRouter.DestroyContext</c>
        /// （淘汰 / CloseAll / 非缓存关闭）时释放。
        /// </summary>
        public IAssetHandle<GameObject> PrefabHandle;

        /// <summary>页面根 GameObject。</summary>
        public GameObject Instance;

        /// <summary>生命周期驱动者：有 Mediator 时是 Mediator，否则是 View。</summary>
        public IUIPage Page;

        /// <summary>视图（可能为空）。</summary>
        public UIPageView View;

        /// <summary>中介者（可能为空）。</summary>
        public UIPageMediator Mediator;

        /// <summary>数据代理（可能为空）。</summary>
        public UIPageProxy Proxy;

        /// <summary>外部句柄，回指用。</summary>
        public UIPageHandle Handle;

        /// <summary>是否处于打开状态（已 OnOpen，未 OnClose）。</summary>
        public bool IsOpen;

        public string PageId
        {
            get { return Def != null ? Def.PageId : null; }
        }

        /// <summary>同一页面的多实例名：PageId 或 PageId#InstanceId。</summary>
        public string InstanceName
        {
            get
            {
                if (Def == null)
                {
                    return string.Empty;
                }

                return Def.Singleton ? Def.PageId : string.Format("{0}#{1}", Def.PageId, InstanceId);
            }
        }

        public override string ToString()
        {
            return InstanceName;
        }
    }
}
