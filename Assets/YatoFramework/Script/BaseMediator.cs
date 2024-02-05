using PureMVC.Core;
using PureMVC.Patterns.Mediator;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.PackageManager;
using UnityEngine;

namespace YatoUIFramework
{

    public class BaseMediator : Mediator
    {
        public int _uuid;
        public PageInfo _pageInfo;
        public BaseUIView _viewComponent;
        public BaseProxy _proxy;
        public GameObject gameObjectRoot;
        public string _gameObjectRootName;
        public Transform _parent;
        public string name;
        public BaseMediator(int uuid, string mediatorName, PageInfo pageInfo) : base(mediatorName)
        {
            _uuid = uuid;
            _pageInfo = pageInfo;
            _parent = UIManager.Instance.UICanvas;//这里应该是UIManager的Root节点
            name = mediatorName;
            gameObjectRoot = null;
            //TODO：异步创建的处理

            //绑定对应的Prefab目前时从Resource里面加载
            var obj = Resources.Load(pageInfo.name);
            gameObjectRoot = (GameObject)GameObject.Instantiate(obj);
            gameObjectRoot.SetActive(false);

            //设置父节点,初始位置
            gameObjectRoot.transform.SetParent(_parent);
            gameObjectRoot.transform.localPosition = Vector3.zero;
            gameObjectRoot.transform.localRotation = Quaternion.identity;
        }


        public virtual void OnInit() { }

        public override void OnRegister()
        {
            base.OnRegister();
            Debug.Log("Register view :"+ name);
            _InitViews();
            OnInit();
        }

        #region 对外方法

        public BaseUIView GetViewComponent()
        {
            if (_viewComponent == null)
            {
                Debug.LogError("cant find viewComponent");
                return null;
            }

            return _viewComponent;
        }

        public void SetViewComponent(BaseUIView viewComponent)
        {
            _viewComponent = viewComponent;
        }

        public BaseProxy GetProxy()
        {
            return _proxy;
        }

        public void SetProxy(BaseProxy proxy)
        {
            _proxy = proxy;
        }

        public void SetParent(Transform parent)
        {
            _parent = parent;
        }

        public Transform GetParent() { return _parent; }

        /// <summary>
        /// 一些UI会有一些缓存数据，原计划这部分数据会通过这个方法在UI再打开时设置
        /// </summary>
        public void SetCurrencyData() { }

        public void SetAllReady() { }

        #endregion

        #region 对内方法
        public void _SetPageInfo()
        {
            if (_parent == null)
            {
                _parent = GameObject.Find("UI").transform;
            }
            SetParent(_parent);
        }
        public void _SetViewInitInfo() { }
        public void _InitViews()
        {
            string className = string.Format("{0}.{1}View", _pageInfo.module, _pageInfo.name); //_pageInfo.name + "View";
            Type type = Type.GetType(className);
            Assembly assembly = Assembly.GetAssembly(type);

            ViewInitInfo viewInitInfo = new ViewInitInfo(_parent.gameObject, SetViewComponent);
            object[] objs = new object[2];
            objs[0] = gameObjectRoot;
            objs[1] = viewInitInfo;

            //根据类名称，通过反射获取有参数的类实例
            BaseUIView uiView = (BaseUIView)assembly.CreateInstance(className, false, BindingFlags.CreateInstance, null, objs, null, null);
        }
        #endregion
    }
}


