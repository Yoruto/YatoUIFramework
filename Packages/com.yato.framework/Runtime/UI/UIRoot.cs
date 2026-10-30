using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yato.UI
{
    /// <summary>
    /// UI 根节点：DontDestroyOnLoad，按层级创建独立 Canvas（文档 7.1）。
    /// 首次访问时自动创建，不依赖场景里预摆的 UI/Canvas（DEF-01/UIManager 反射查找的替代）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour
    {
        private const string RootObjectName = "[Yato.UI]";

        /// <summary>设计分辨率。</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1080f, 1920f);

        private static UIRoot s_instance;

        private readonly Dictionary<UILayer, UILayerRoot> m_layers =
            new Dictionary<UILayer, UILayerRoot>();

        /// <summary>UI 相机；Screen Space Overlay 模式下可为空。</summary>
        public Camera UICamera;

        public static UIRoot Instance
        {
            get
            {
                if (s_instance == null)
                {
                    EnsureCreated();
                }

                return s_instance;
            }
        }

        public static bool IsAlive
        {
            get { return s_instance != null; }
        }

        private static void EnsureCreated()
        {
            if (s_instance != null)
            {
                return;
            }

            var go = new GameObject(RootObjectName, typeof(RectTransform));
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<UIRoot>();
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);
            BuildLayers();
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        /// <summary>取得某层根节点；不存在时按需创建。</summary>
        public UILayerRoot GetLayerRoot(UILayer layer)
        {
            UILayerRoot root;
            if (m_layers.TryGetValue(layer, out root) && root != null)
            {
                return root;
            }

            root = UILayerRoot.Create(transform, layer, SortingOrderOf(layer), ReferenceResolution);
            m_layers[layer] = root;
            return root;
        }

        /// <summary>页面挂载点。</summary>
        public Transform GetContainer(UILayer layer)
        {
            return GetLayerRoot(layer).Container;
        }

        /// <summary>层级对应的 Canvas sortingOrder。</summary>
        public static int SortingOrderOf(UILayer layer)
        {
            switch (layer)
            {
                case UILayer.Background:
                    return 0;
                case UILayer.Normal:
                    return 100;
                case UILayer.Popup:
                    return 200;
                case UILayer.Tips:
                    return 300;
                case UILayer.System:
                    return 400;
                default:
                    throw new ArgumentOutOfRangeException("layer", layer, "未知 UI 层级");
            }
        }

        private void BuildLayers()
        {
            Array values = Enum.GetValues(typeof(UILayer));
            for (int i = 0; i < values.Length; i++)
            {
                GetLayerRoot((UILayer)values.GetValue(i));
            }
        }
    }
}
