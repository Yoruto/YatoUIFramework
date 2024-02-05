using PureMVC.Interfaces;
using PureMVC.Patterns.Facade;
using PureMVC.Patterns.Observer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.VisualScripting;
using UnityEngine;
namespace YatoUIFramework
{
    public class UIManager : SingletonFacade<UIManager>
    {
        public Transform UICanvas;
        public Camera UICamera;
        private int uuid = 0;

        //存储同步生成UI的队列
        private List<SyncCreateUIData> _syncCreateUIList = new List<SyncCreateUIData>();

        public UIManager()
        {
            ///这部分可以优化
            Debug.Log("UIMananger Singleton Facade Ctor");
            UICanvas = GameObject.Find("UI/Canvas").transform;
            UICamera = GameObject.Find("UICamera").GetComponent<Camera>();
            RegisterCommand("ValueChange", () => { return new ExampleValueChangeCommand(); });
            RegisterCommand("CreateUICommand", () => { return new CreateUICommand(); });
        }

        #region Core
        public void Update(float deltaTime)
        {
            _UpdateAsyncOperation(deltaTime);
        }
        #endregion

        #region 外部方法

        /// <summary>
        /// 这个是同步方案
        /// </summary>
        /// <param name="PageInfo"></param>
        /// <param name="uiParams"></param>
        /// <param name="uiParent"></param>
        /// <param name="isSubMediator"></param>
        /// <param name="gotoInfo"></param>
        /// <returns></returns>
        public BaseMediator CreateUI(PageInfo PageInfo, object[] uiParams, GameObject uiParent, bool isSubMediator, bool gotoInfo)
        {
            return _CreateUI(PageInfo, uiParams, isSubMediator, gotoInfo);
        }

        public BaseMediator CreateMediatorClass(PageInfo PageInfo, object[] args)
        {
            //类名称，格式：命名空间.类名
            string className = string.Format("{0}.{1}Mediator", PageInfo.module, PageInfo.name); //PageInfo.module +"."+ PageInfo.name + "Mediator";
            Type type = Type.GetType(className);
            Assembly assembly = Assembly.GetAssembly(type);
            //根据类名称，通过反射获取有参数的类实例
            BaseMediator mediator = (BaseMediator)assembly.CreateInstance(className, false, BindingFlags.CreateInstance, null, args, null, null);
            return mediator;
        }

        public BaseProxy CreateProxyClass(PageInfo PageInfo, object[] uiParams)
        {
            string className = string.Format("{0}.{1}Proxy", PageInfo.module, PageInfo.name);  //PageInfo.name + "Proxy";
            Type type = Type.GetType(className);
            Assembly assembly = Assembly.GetAssembly(type);
            BaseProxy proxy = (BaseProxy)assembly.CreateInstance(className, false, BindingFlags.CreateInstance, null, uiParams, null, null);
            return proxy;
        }


        #endregion

        #region 内部方法

        public BaseMediator _CreateUI(PageInfo PageInfo, object[] uiParams, bool isSubMediator, bool gotoInfo)
        {

            object[] args = new object[3];
            args[0] = uuid;
            args[1] = PageInfo.name;
            args[2] = PageInfo;
            BaseMediator mediatorInstance = CreateMediatorClass(PageInfo, args);
            BaseProxy proxy = CreateProxyClass(PageInfo, uiParams);
            mediatorInstance.SetProxy(proxy);
            SyncCreateUIData tempData = new SyncCreateUIData(mediatorInstance);
            _syncCreateUIList.Add(tempData);
            uuid++;
            return mediatorInstance;
        }

        private void _UpdateAsyncOperation(float deltaTime)
        {
            _UpdateAsyncDestroyUI();
            _UpdateSyncCreateUI();
            _UpdateAsyncCreateUI();
        }

        private void _UpdateAsyncDestroyUI()
        {
            //TODO
        }

        private void _UpdateSyncCreateUI()
        {
            if (_syncCreateUIList.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _syncCreateUIList.Count; i++)
            {
                BaseMediator ui = _syncCreateUIList[i].ui;
                _InitializeUI(ui);
            }

            _syncCreateUIList.Clear();
        }

        private void _UpdateAsyncCreateUI()
        {
            //TODO
        }
        private void _InitializeUI(BaseMediator ui)
        {
            Debug.Log("InitializeUI" + ui.name);
            ui.gameObjectRoot.SetActive(true);
            RegisterMediator(ui);
            RegisterProxy(ui.GetProxy());
            ui.SetAllReady();
            _AddUIToManager(ui);
        }

        public void _AddUIToManager(BaseMediator ui)
        {
            //TODO
        }

        #endregion
    }

    public class SyncCreateUIData
    {
        public BaseMediator ui;
        public BaseMediator uiparent;
        public Action uiCallBack;

        public SyncCreateUIData(BaseMediator ui, BaseMediator uiparent = null, Action uiCallBack = null)
        {
            this.ui = ui;
            this.uiparent = uiparent;
            this.uiCallBack = uiCallBack;
        }
    }
}