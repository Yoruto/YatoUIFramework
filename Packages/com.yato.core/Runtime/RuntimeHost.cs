using System;
using System.Collections;
using UnityEngine;

namespace Yato.Core
{
    /// <summary>
    /// 框架唯一的运行时宿主：负责 DontDestroyOnLoad、每帧驱动与协程托管。
    /// 由 <see cref="Bootstrap"/> 在场景加载前自动创建，业务代码无需再手写
    /// <c>UIManager.Instance.Update(deltaTime)</c> 之类的帧调用。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeHost : MonoBehaviour
    {
        private const string HostObjectName = "[Yato.RuntimeHost]";

        private static RuntimeHost s_instance;

        /// <summary>宿主实例；首次访问时自动创建。</summary>
        public static RuntimeHost Instance
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

        /// <summary>每帧回调（主线程），参数为 <see cref="Time.deltaTime"/>。</summary>
        public static event Action<float> Updated;

        /// <summary>每帧 LateUpdate 回调（主线程），参数为 <see cref="Time.deltaTime"/>。</summary>
        public static event Action<float> LateUpdated;

        /// <summary>当前是否能拿到宿主（用于退出流程中避免重建对象）。</summary>
        public static bool IsAlive
        {
            get { return s_instance != null; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureCreated();
            MainThreadDispatcher.Initialize();
        }

        private static void EnsureCreated()
        {
            if (s_instance != null)
            {
                return;
            }

            var host = new GameObject(HostObjectName);
            DontDestroyOnLoad(host);
            s_instance = host.AddComponent<RuntimeHost>();
        }

        /// <summary>在宿主上启动协程；宿主不存在时自动创建。</summary>
        public static Coroutine RunCoroutine(IEnumerator routine)
        {
            if (routine == null)
            {
                return null;
            }

            return Instance.StartCoroutine(routine);
        }

        /// <summary>安全停止协程，宿主或协程为空时直接忽略。</summary>
        public static void StopCoroutineSafe(Coroutine coroutine)
        {
            if (coroutine == null || s_instance == null)
            {
                return;
            }

            s_instance.StopCoroutine(coroutine);
        }

        private void Update()
        {
            InvokeSafely(Updated, "Update");
        }

        private void LateUpdate()
        {
            InvokeSafely(LateUpdated, "LateUpdate");
        }

        /// <summary>逐个回调并隔离异常，避免一个订阅者中断整帧驱动。</summary>
        private static void InvokeSafely(Action<float> handlers, string phase)
        {
            if (handlers == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            Delegate[] invocationList = handlers.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                var handler = (Action<float>)invocationList[i];
                try
                {
                    handler(deltaTime);
                }
                catch (Exception e)
                {
                    Log.Error(
                        "RuntimeHost",
                        string.Format(
                            "{0} 回调异常：{1}.{2}",
                            phase,
                            handler.Method.DeclaringType != null ? handler.Method.DeclaringType.FullName : "?",
                            handler.Method.Name),
                        e);
                }
            }
        }

        /// <summary>
        /// 关闭 Domain Reload 时静态状态不会自动清空；进入 Play 前必须重置，
        /// 否则上一会话的订阅者会挂在静态事件上（对应文档 D02 的 Domain Reload 要求）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            Updated = null;
            LateUpdated = null;
        }

        private void OnDestroy()
        {
            if (s_instance != this)
            {
                return;
            }

            // 宿主销毁后继续向旧订阅者派发只会访问已销毁对象，统一清空
            Updated = null;
            LateUpdated = null;
            s_instance = null;
        }
    }
}
