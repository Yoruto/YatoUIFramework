using TMPro;
using UnityEngine;
using Yato.UI;

namespace Yato.FrameworkDemo
{
    /// <summary>
    /// 页面视图：作为 MonoBehaviour 挂在预制体上（或由工厂按 Mediator 泛型参数自动补挂）。
    /// 生命周期回调全部在主线程、按 Create → Open → Cover/Reveal → Close 顺序触发。
    /// </summary>
    public class ExamplePageView : UIPageView
    {
        private TextMeshProUGUI m_title;

        protected override void Awake()
        {
            base.Awake();
            // 绑定在 Awake 完成，不再依赖构造期回调（DEF-07）
            m_title = GetComponentInChildren<TextMeshProUGUI>(true);
        }

        public override void OnOpen(object args)
        {
            Refresh(args);
        }

        /// <summary>刷新显示（OnOpen 与 Proxy.RefreshView 共用）。</summary>
        public void Refresh(object data)
        {
            string text = data as string;
            if (m_title != null)
            {
                m_title.text = string.IsNullOrEmpty(text) ? "Example" : text;
            }

            Debug.LogFormat("[Demo] Refresh({0})", text);
        }

        public override void OnCover()
        {
            Debug.Log("[Demo] OnCover：被上层遮住，暂停输入");
        }

        public override void OnReveal()
        {
            Debug.Log("[Demo] OnReveal：重新露出，刷新数据");
        }

        public override void OnClose(bool destroy)
        {
            Debug.LogFormat("[Demo] OnClose(destroy={0})", destroy);
        }
    }
}
