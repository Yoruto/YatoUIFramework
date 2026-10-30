using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yato.UI
{
    /// <summary>
    /// 旧版组件绑定工具（兼容保留，预制体上的数据仍可读取）。
    /// 它的 <c>GetBindComponent&lt;T&gt;(int index)</c> 依赖索引，节点重排就会静默绑错（DEF-13），
    /// 新页面请使用 <see cref="ComponentBinder"/>（按名称查找 + 运行时校验）。
    /// 该类会在后续版本标记为 Obsolete 并移除。
    /// </summary>
    public class ComponentAutoBindTool : MonoBehaviour
    {
#if UNITY_EDITOR
        [Serializable]
        public class BindData
        {
            public BindData()
            {
            }

            public BindData(string name, Component bindCom)
            {
                Name = name;
                BindCom = bindCom;
            }

            public string Name;
            public Component BindCom;
        }

        public List<BindData> BindDatas = new List<BindData>();

        [SerializeField]
        private string m_ClassName;

        [SerializeField]
        private string m_Namespace;

        [SerializeField]
        private string m_CodePath;

        public string ClassName
        {
            get { return m_ClassName; }
        }

        public string Namespace
        {
            get { return m_Namespace; }
        }

        public string CodePath
        {
            get { return m_CodePath; }
        }
#endif

        [SerializeField]
        public List<Component> m_BindComs = new List<Component>();

        /// <summary>按索引取组件（不推荐，DEF-13）。</summary>
        public T GetBindComponent<T>(int index) where T : Component
        {
            if (index < 0 || index >= m_BindComs.Count)
            {
                Debug.LogErrorFormat("[Yato.UI] {0} 绑定索引越界：{1}/{2}", name, index, m_BindComs.Count);
                return null;
            }

            var bindCom = m_BindComs[index] as T;
            if (bindCom == null)
            {
                Debug.LogErrorFormat(
                    "[Yato.UI] {0} 第 {1} 个绑定类型不匹配，期望 {2}",
                    name,
                    index,
                    typeof(T).Name);
            }

            return bindCom;
        }
    }
}
