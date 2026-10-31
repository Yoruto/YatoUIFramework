using UnityEngine;
using UnityEngine.UI;

namespace Yato.UI
{
    /// <summary>单个层级的根节点：独立 Canvas + CanvasScaler + 射线检测。</summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class UILayerRoot : MonoBehaviour
    {
        /// <summary>所属层级。</summary>
        public UILayer Layer { get; internal set; }

        /// <summary>本层 Canvas（用于调整 sortingOrder / 相机）。</summary>
        public Canvas Canvas { get; internal set; }

        public RectTransform RectTransform
        {
            get { return (RectTransform)transform; }
        }

        /// <summary>页面的挂载点（与层根重合）。</summary>
        public Transform Container
        {
            get { return transform; }
        }

        internal static UILayerRoot Create(Transform parent, UILayer layer, int sortingOrder, Vector2 referenceResolution)
        {
            var go = new GameObject(
                string.Format("Layer_{0}", layer),
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            go.transform.SetParent(parent, false);

            var root = go.AddComponent<UILayerRoot>();
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            root.Layer = layer;
            root.Canvas = canvas;
            return root;
        }
    }
}
