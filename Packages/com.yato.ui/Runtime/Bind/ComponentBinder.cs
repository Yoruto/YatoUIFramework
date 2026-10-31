using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yato.UI
{
    /// <summary>
    /// 按名称绑定组件（替代 <see cref="ComponentAutoBindTool"/> 的索引绑定，修复 DEF-13）。
    /// 名称建议与节点同名（<c>Btn_Close</c> / <c>Txt_Title</c>），重排节点不会绑错；
    /// 取值时做类型校验与缺失校验，并给出具体的 prefab / 名称。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ComponentBinder : MonoBehaviour
    {
        [Serializable]
        public sealed class BindEntry
        {
            public string Name;
            public Component Target;
        }

        [SerializeField]
        private List<BindEntry> m_bindings = new List<BindEntry>();

        private Dictionary<string, Component> m_map;

        /// <summary>原始绑定列表（编辑器 / 校验用）。</summary>
        public IList<BindEntry> Bindings
        {
            get { return m_bindings; }
        }

        /// <summary>按名称取组件；缺失或类型不符时返回 null 并记录错误。</summary>
        public T Get<T>(string bindName) where T : Component
        {
            T component;
            if (TryGet(bindName, out component))
            {
                return component;
            }

            Debug.LogErrorFormat(
                "[Yato.UI] {0} 缺少绑定：{1}（期望 {2}），请检查预制体 {3}",
                name,
                bindName,
                typeof(T).Name,
                GetPrefabName());
            return null;
        }

        /// <summary>按名称取组件，不打印错误（用于可选绑定）。</summary>
        public bool TryGet<T>(string bindName, out T component) where T : Component
        {
            component = null;
            if (string.IsNullOrEmpty(bindName))
            {
                return false;
            }

            EnsureMap();

            Component target;
            if (!m_map.TryGetValue(bindName, out target) || target == null)
            {
                return false;
            }

            component = target as T;
            return component != null;
        }

        /// <summary>
        /// 校验绑定表：空名称、重名、空引用。返回 false 时 <paramref name="error"/> 给出原因。
        /// </summary>
        public bool Validate(out string error)
        {
            error = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < m_bindings.Count; i++)
            {
                BindEntry entry = m_bindings[i];
                if (entry == null)
                {
                    error = string.Format("{0} 第 {1} 条绑定为空", GetPrefabName(), i);
                    return false;
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    error = string.Format("{0} 第 {1} 条绑定缺少名称", GetPrefabName(), i);
                    return false;
                }

                if (!seen.Add(entry.Name))
                {
                    error = string.Format("{0} 存在重名绑定：{1}", GetPrefabName(), entry.Name);
                    return false;
                }

                if (entry.Target == null)
                {
                    error = string.Format("{0} 的绑定 {1} 目标为空", GetPrefabName(), entry.Name);
                    return false;
                }
            }

            return true;
        }

        /// <summary>新增/覆盖一条绑定（代码生成器使用）。</summary>
        public void Add(string bindName, Component target)
        {
            if (string.IsNullOrEmpty(bindName) || target == null)
            {
                return;
            }

            for (int i = 0; i < m_bindings.Count; i++)
            {
                if (m_bindings[i] != null && m_bindings[i].Name == bindName)
                {
                    m_bindings[i].Target = target;
                    m_map = null;
                    return;
                }
            }

            m_bindings.Add(new BindEntry { Name = bindName, Target = target });
            m_map = null;
        }

        private void Awake()
        {
            EnsureMap();
        }

        private void EnsureMap()
        {
            if (m_map != null)
            {
                return;
            }

            m_map = new Dictionary<string, Component>(m_bindings.Count, StringComparer.Ordinal);
            for (int i = 0; i < m_bindings.Count; i++)
            {
                BindEntry entry = m_bindings[i];
                if (entry == null || string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                if (m_map.ContainsKey(entry.Name))
                {
                    Debug.LogErrorFormat("[Yato.UI] {0} 存在重复绑定名：{1}", GetPrefabName(), entry.Name);
                    continue;
                }

                m_map[entry.Name] = entry.Target;
            }
        }

        private string GetPrefabName()
        {
            // 运行时 name 会带 (Clone)，去掉便于定位
            string runtimeName = name;
            int index = runtimeName.IndexOf("(Clone)", StringComparison.Ordinal);
            return index >= 0 ? runtimeName.Substring(0, index).Trim() : runtimeName;
        }
    }
}
