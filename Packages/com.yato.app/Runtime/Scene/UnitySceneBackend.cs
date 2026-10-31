using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yato.Core;

namespace Yato.App
{
    /// <summary>默认场景后端：直接调用 Unity SceneManager。</summary>
    public sealed class UnitySceneBackend : ISceneBackend
    {
        public bool CanLoad(string sceneNameOrPath)
        {
            if (string.IsNullOrEmpty(sceneNameOrPath))
            {
                return false;
            }

            try
            {
                return Application.CanStreamedLevelBeLoaded(sceneNameOrPath);
            }
            catch (Exception e)
            {
                Log.Error("UnitySceneBackend", "检查场景可加载性失败：" + sceneNameOrPath, e);
                return false;
            }
        }

        public ISceneOperation LoadScene(string sceneNameOrPath, LoadSceneMode mode)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneNameOrPath, mode);
            if (operation == null)
            {
                throw new InvalidOperationException("SceneManager 未返回加载操作：" + sceneNameOrPath);
            }

            return new UnitySceneOperation(operation);
        }

        public ISceneOperation UnloadScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return CompletedSceneOperation.Instance;
            }

            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
            return operation == null ? (ISceneOperation)CompletedSceneOperation.Instance : new UnitySceneOperation(operation);
        }

        public Scene FindLoadedScene(string sceneNameOrPath)
        {
            if (string.IsNullOrEmpty(sceneNameOrPath))
            {
                return default(Scene);
            }

            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                if (string.Equals(scene.name, sceneNameOrPath, StringComparison.Ordinal) ||
                    string.Equals(scene.path, sceneNameOrPath, StringComparison.OrdinalIgnoreCase))
                {
                    return scene;
                }
            }

            // 单场景加载后工程里只剩一个已加载场景，可作为兜底
            if (sceneCount == 1)
            {
                Scene only = SceneManager.GetSceneAt(0);
                if (only.isLoaded)
                {
                    return only;
                }
            }

            return default(Scene);
        }

        public bool SetActiveScene(Scene scene)
        {
            return scene.IsValid() && SceneManager.SetActiveScene(scene);
        }

        public int LoadedSceneCount
        {
            get
            {
                int count = 0;
                int sceneCount = SceneManager.sceneCount;
                for (int i = 0; i < sceneCount; i++)
                {
                    if (SceneManager.GetSceneAt(i).isLoaded)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    /// <summary>包装 <see cref="AsyncOperation"/>。</summary>
    internal sealed class UnitySceneOperation : ISceneOperation
    {
        private readonly AsyncOperation m_operation;

        public UnitySceneOperation(AsyncOperation operation)
        {
            m_operation = operation;
        }

        public bool IsDone
        {
            get { return m_operation == null || m_operation.isDone; }
        }

        public float Progress
        {
            get { return m_operation == null ? 1f : m_operation.progress; }
        }
    }

    /// <summary>已经是完成态的占位操作。</summary>
    internal sealed class CompletedSceneOperation : ISceneOperation
    {
        public static readonly CompletedSceneOperation Instance = new CompletedSceneOperation();

        private CompletedSceneOperation()
        {
        }

        public bool IsDone
        {
            get { return true; }
        }

        public float Progress
        {
            get { return 1f; }
        }
    }
}
