using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Yato.Core
{
    /// <summary>
    /// Unity <see cref="AsyncOperation"/> 与协程/回调之间的适配。
    /// 资源层不再手写「while (!op.isDone) yield return null」。
    /// </summary>
    public static class AsyncOperationExtensions
    {
        /// <summary>等待单个操作完成。</summary>
        public static IEnumerator WaitUntilDone(this AsyncOperation operation)
        {
            if (operation == null)
            {
                yield break;
            }

            while (!operation.isDone)
            {
                yield return null;
            }
        }

        /// <summary>等待一组操作全部完成。</summary>
        public static IEnumerator WaitAll(IList<AsyncOperation> operations)
        {
            if (operations == null || operations.Count == 0)
            {
                yield break;
            }

            while (true)
            {
                bool anyPending = false;
                for (int i = 0; i < operations.Count; i++)
                {
                    AsyncOperation operation = operations[i];
                    if (operation != null && !operation.isDone)
                    {
                        anyPending = true;
                        break;
                    }
                }

                if (!anyPending)
                {
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// 操作完成后回调（在主线程执行）。操作已完成为空时立即回调。
        /// </summary>
        public static void OnCompleted(this AsyncOperation operation, Action onCompleted)
        {
            if (onCompleted == null)
            {
                return;
            }

            if (operation == null || operation.isDone)
            {
                onCompleted();
                return;
            }

            RuntimeHost.RunCoroutine(WaitAndInvoke(operation, onCompleted));
        }

        /// <summary>在 RuntimeHost 上启动协程。</summary>
        public static Coroutine Run(this IEnumerator routine)
        {
            return RuntimeHost.RunCoroutine(routine);
        }

        private static IEnumerator WaitAndInvoke(AsyncOperation operation, Action onCompleted)
        {
            while (!operation.isDone)
            {
                yield return null;
            }

            try
            {
                onCompleted();
            }
            catch (Exception e)
            {
                Log.Error("AsyncOperation", "完成回调异常", e);
            }
        }
    }
}
