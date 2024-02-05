using PureMVC.Patterns.Facade;
using Unity.VisualScripting;
using UnityEngine;

namespace YatoUIFramework
{
    public class Singleton<T> where T : new()
    {
        static protected T sInstance;
        static protected bool IsCreate = false;

        public static T Instance
        {
            get
            {
                if (IsCreate == false)
                {
                    Debug.LogErrorFormat("instance is null ! T:{0}", typeof(T).ToString());
                    CreateInstance();
                }

                return sInstance;
            }
        }

        public static void CreateInstance()
        {
            if (IsCreate == true)
            {
                Debug.LogErrorFormat("instance has created ! T:{0}", typeof(T).ToString());
                return;
            }

            IsCreate = true;
            sInstance = new T();
        }

        public static void ReleaseInstance()
        {
            sInstance = default(T);
            IsCreate = false;
        }
    }
}

public abstract class SingletonFacade<T> : Facade where T : Facade,new()
{
    static protected T sInstance;
    static protected bool IsCreate = false;

    public static T Instance
    {
        get
        {
            if (IsCreate == false)
            {
                Debug.LogErrorFormat("instance is null ! T:{0}", typeof(T).ToString());
                CreateInstance();
            }

            return sInstance;
        }
    }

    public static void CreateInstance()
    {
        if (IsCreate == true)
        {
            Debug.LogErrorFormat("instance has created ! T:{0}", typeof(T).ToString());
            return;
        }

        IsCreate = true;
        sInstance = new T();
    }

    public static void ReleaseInstance()
    {
        sInstance = default(T);
        IsCreate = false;
    }
}

public abstract class SingletonMonoBehaviour<T> : MonoBehaviour where T : SingletonMonoBehaviour<T>
{
    protected static T sInstance = null;
    protected static bool IsCreate = false;
    public static bool s_debugDestroy = false;
    public static T Instance
    {
        get
        {
            if (s_debugDestroy)
            {
                return null;
            }
            CreateInstance();
            return sInstance;
        }
    }

    protected virtual void Awake()
    {
        if (sInstance == null)
        {
            sInstance = this as T;
            IsCreate = true;

            AwakeInit();
        }
    }

    protected virtual void FirstInit()
    {

    }

    protected virtual void AwakeInit()
    {

    }

    protected virtual void OnDestroy()
    {
        sInstance = null;
        IsCreate = false;
    }

    private void OnApplicationQuit()
    {
        sInstance = null;
        IsCreate = false;

        Quit();
    }

    protected virtual void Quit()
    {

    }

    public static void CreateInstance(GameObject go = null)
    {
        if (IsCreate == true)
            return;

        IsCreate = true;
        T[] managers = GameObject.FindObjectsOfType(typeof(T)) as T[];
        if (managers.Length != 0)
        {
            if (managers.Length == 1)
            {
                sInstance = managers[0];
                sInstance.FirstInit();

                DontDestroyOnLoad(sInstance.gameObject);
                return;
            }
            else
            {
                foreach (T manager in managers)
                {
                    Destroy(manager.gameObject);
                }
            }
        }

        if (go != null)
        {
            sInstance = go.AddComponent<T>();
        }
        else
        {
            go = new GameObject(typeof(T).Name, typeof(T));
            sInstance = go.GetComponent<T>();
        }
        sInstance.FirstInit();
        DontDestroyOnLoad(sInstance.gameObject);
    }

    public static void ReleaseInstance(bool destroyGameObject)
    {
        if (sInstance != null)
        {
            if (destroyGameObject)
            {
                Destroy(sInstance.gameObject);
            }
            else
            {
                Destroy(sInstance);
            }
            sInstance = null;
            IsCreate = false;
        }
    }
}
