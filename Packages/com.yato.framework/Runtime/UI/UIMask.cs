using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Yato.UI
{
    /// <summary>
    /// 弹窗遮罩：由框架统一生成，不再要求业务预制体自带 CloseBg（DEF-12）。
    /// 点击行为由打开方注入（默认只关闭最上层弹窗，见 T-P3-06）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIMask : MonoBehaviour, IPointerClickHandler
    {
        private const string MaskObjectName = "[Yato.UI.Mask]";

        private static UIMask s_instance;

        private Image m_image;
        private Action m_onClick;

        public static UIMask Instance
        {
            get { return s_instance; }
        }

        public bool IsVisible
        {
            get { return gameObject.activeSelf; }
        }

        /// <summary>遮罩底图（用于调整透明度 / 换图）。</summary>
        public Image Image
        {
            get { return m_image; }
        }

        /// <summary>显示遮罩并绑定点击行为。</summary>
        public static UIMask Show(Action onClick)
        {
            UIMask mask = EnsureCreated();
            mask.m_onClick = onClick;
            mask.gameObject.SetActive(true);
            // 保持在弹窗层的最底部，避免遮挡弹窗内容
            mask.transform.SetAsFirstSibling();
            return mask;
        }

        public static void Hide()
        {
            if (s_instance != null)
            {
                s_instance.m_onClick = null;
                s_instance.gameObject.SetActive(false);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Action handler = m_onClick;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception e)
            {
                Yato.Core.Log.Error("UIMask", "遮罩点击回调异常", e);
            }
        }

        private static UIMask EnsureCreated()
        {
            if (s_instance != null)
            {
                return s_instance;
            }

            Transform parent = UIRoot.Instance.GetContainer(UILayer.Popup);
            var go = new GameObject(MaskObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.55f);
            image.raycastTarget = true;

            var mask = go.AddComponent<UIMask>();
            mask.m_image = image;
            s_instance = mask;
            go.SetActive(false);
            return mask;
        }
    }
}
