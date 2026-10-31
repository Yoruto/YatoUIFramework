using UnityEngine;

namespace Yato.App
{
    /// <summary>
    /// 应用启动配置（可选）。放在 <c>Resources/Yato/AppBootstrapConfig</c> 时，
    /// 编辑器下从任意场景直接 Play 会自动读取它并走与 Bootstrap Scene 相同的初始化路径。
    /// </summary>
    [CreateAssetMenu(menuName = "Yato/App Bootstrap Config", fileName = "AppBootstrapConfig")]
    public sealed class AppBootstrapConfig : ScriptableObject
    {
        /// <summary>编辑器自动初始化时使用的资源路径（不含扩展名）。</summary>
        public const string DefaultResourcePath = "Yato/AppBootstrapConfig";

        [SerializeField] private bool m_autoInitializeInEditor = true;

        [SerializeField] private float m_defaultSceneReadyTimeoutSeconds = 60f;

        /// <summary>编辑器下从任意场景 Play 时是否自动补初始化。</summary>
        public bool AutoInitializeInEditor
        {
            get { return m_autoInitializeInEditor; }
            set { m_autoInitializeInEditor = value; }
        }

        /// <summary>场景 Ready 初始化器的默认超时（秒）。</summary>
        public float DefaultSceneReadyTimeoutSeconds
        {
            get { return m_defaultSceneReadyTimeoutSeconds > 0f ? m_defaultSceneReadyTimeoutSeconds : 60f; }
            set { m_defaultSceneReadyTimeoutSeconds = value; }
        }
    }
}
