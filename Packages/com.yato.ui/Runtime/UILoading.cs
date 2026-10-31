using UnityEngine;
using UnityEngine.UI;

namespace Yato.UI
{
    /// <summary>
    /// 加载遮罩（System 层）：由框架按需生成，业务无需自带 Loading 预制体。
    /// 打开耗时超过阈值时自动出现，完成后自动隐藏（T-P3-02）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UILoading : MonoBehaviour
    {
        private const string ObjectName = "[Yato.UI.Loading]";

        private static UILoading s_instance;

        private CanvasGroup m_group;
        private Text m_label;
        private Image m_progressFill;

        public static bool IsVisible
        {
            get { return s_instance != null && s_instance.gameObject.activeSelf; }
        }

        /// <summary>显示；<paramref name="progress"/> 小于 0 时不展示进度条。</summary>
        public static void Show(string message = "加载中...", float progress = -1f)
        {
            UILoading loading = EnsureCreated();
            loading.gameObject.SetActive(true);
            loading.transform.SetAsLastSibling();
            loading.SetText(message);
            loading.ApplyProgress(progress);
        }

        public static void Hide()
        {
            if (s_instance != null)
            {
                s_instance.gameObject.SetActive(false);
            }
        }

        public static void SetProgress(float progress)
        {
            if (s_instance == null)
            {
                return;
            }

            s_instance.ApplyProgress(progress);
        }

        private void SetText(string message)
        {
            if (m_label != null)
            {
                m_label.text = string.IsNullOrEmpty(message) ? "加载中..." : message;
            }
        }

        private void ApplyProgress(float progress)
        {
            if (m_progressFill == null)
            {
                return;
            }

            bool visible = progress >= 0f;
            m_progressFill.transform.parent.gameObject.SetActive(visible);
            if (visible)
            {
                m_progressFill.fillAmount = Mathf.Clamp01(progress);
            }
        }

        private static UILoading EnsureCreated()
        {
            if (s_instance != null)
            {
                return s_instance;
            }

            Transform parent = UIRoot.Instance.GetContainer(UILayer.System);

            var root = new GameObject(ObjectName, typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(parent, false);
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var background = new GameObject("Bg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            background.transform.SetParent(root.transform, false);
            Stretch((RectTransform)background.transform);
            var bgImage = background.GetComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.6f);
            bgImage.raycastTarget = true;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(root.transform, false);
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            labelRect.anchorMax = new Vector2(1f, 0.5f);
            labelRect.sizeDelta = new Vector2(0f, 60f);
            var label = labelObject.GetComponent<Text>();
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = 32;
            label.color = Color.white;
            label.text = "加载中...";
            label.font = LoadDefaultFont();

            var barRoot = new GameObject("Progress", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            barRoot.transform.SetParent(root.transform, false);
            var barRect = (RectTransform)barRoot.transform;
            barRect.anchorMin = new Vector2(0.2f, 0.42f);
            barRect.anchorMax = new Vector2(0.8f, 0.45f);
            barRect.offsetMin = Vector2.zero;
            barRect.offsetMax = Vector2.zero;
            barRoot.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fill.transform.SetParent(barRoot.transform, false);
            Stretch((RectTransform)fill.transform);
            var fillImage = fill.GetComponent<Image>();
            fillImage.color = new Color(0.4f, 0.8f, 1f, 1f);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillAmount = 0f;

            var loading = root.AddComponent<UILoading>();
            loading.m_group = root.GetComponent<CanvasGroup>();
            loading.m_label = label;
            loading.m_progressFill = fillImage;

            s_instance = loading;
            root.SetActive(false);
            return loading;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Font LoadDefaultFont()
        {
            // Unity 2022 起内置字体更名为 LegacyRuntime.ttf
            // 注意：必须写全 UnityEngine.Resources，否则会解析到 Yato.Resources 命名空间
            Font font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return font;
        }
    }
}
