using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Yato.Core
{
    /// <summary>
    /// 主线程投递器：网络线程 / 异步回调可以安全地把逻辑切回主线程执行。
    /// 队列由 <see cref="RuntimeHost"/> 每帧消费，所有回调都在主线程上执行。
    /// </summary>
    public static class MainThreadDispatcher
    {
        private static readonly Queue<Action> s_pending = new Queue<Action>(64);
        private static readonly Queue<Action> s_completions = new Queue<Action>(32);
        private static readonly object s_gate = new object();

        private static int s_mainThreadId = -1;
        private static bool s_initialized;

        /// <summary>是否已完成初始化（由 RuntimeHost 在场景加载前调用）。</summary>
        public static bool IsInitialized
        {
            get { return s_initialized; }
        }

        /// <summary>当前是否处于主线程。</summary>
        public static bool IsMainThread
        {
            get { return s_initialized && Thread.CurrentThread.ManagedThreadId == s_mainThreadId; }
        }

        /// <summary>记录主线程并接入宿主每帧驱动；重复调用安全。</summary>
        public static void Initialize()
        {
            if (s_initialized)
            {
                return;
            }

            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            RuntimeHost.Updated -= Pump;
            RuntimeHost.Updated += Pump;
            s_initialized = true;
        }

        /// <summary>无条件入队，由主线程在下一帧执行。</summary>
        public static void Post(Action action)
        {
            if (action == null)
            {
                return;
            }

            lock (s_gate)
            {
                s_pending.Enqueue(action);
            }
        }

        /// <summary>
        /// Queue a terminal notification that must survive Clear/host shutdown. Lifecycle owners
        /// must reject stale results themselves; observers are still owed their completion signal.
        /// </summary>
        public static void PostCompletion(Action action)
        {
            if (action == null) return;
            lock (s_gate) s_completions.Enqueue(action);
        }

        /// <summary>已在主线程则立即执行，否则入队。</summary>
        public static void Send(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (IsMainThread)
            {
                action();
                return;
            }

            Post(action);
        }

        /// <summary>清空一般投递；已经承诺的完成通知仍会派发。</summary>
        public static void Clear()
        {
            lock (s_gate)
            {
                s_pending.Clear();
            }
        }

        /// <summary>关闭 Domain Reload 时重置静态状态，避免上一会话的队列与线程标记残留。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Shutdown();

        internal static void Shutdown()
        {
            // Called on the Unity main thread. Use one snapshot so reentrant callbacks cannot
            // turn shutdown into an unbounded drain. Newly queued completions await the next host.
            InvokeBatch(TakeCompletions());
            s_initialized = false;
            s_mainThreadId = -1;
            Clear();
        }

        private static void Pump(float deltaTime)
        {
            InvokeBatch(TakeCompletions());
            Action[] batch;
            lock (s_gate)
            {
                if (s_pending.Count == 0)
                {
                    return;
                }

                batch = s_pending.ToArray();
                s_pending.Clear();
            }

            InvokeBatch(batch);
        }

        private static Action[] TakeCompletions()
        {
            lock (s_gate)
            {
                var batch = s_completions.ToArray();
                s_completions.Clear();
                return batch;
            }
        }

        private static void InvokeBatch(Action[] batch)
        {
            for (int i = 0; i < batch.Length; i++)
            {
                try
                {
                    batch[i]();
                }
                catch (Exception e)
                {
                    Log.Error("MainThreadDispatcher", "主线程投递回调异常", e);
                }
            }
        }
    }
}
