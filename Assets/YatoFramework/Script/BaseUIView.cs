using System;
using UnityEngine;

namespace YatoUIFramework
{

    public class BaseUIView : BaseUI
    {
        public ViewInitInfo _viewInitInfo;
        public GameObject _parentGameObject;
        public GameObject gameObjectRoot;
        public BaseUIView(GameObject gameObjectRoot, ViewInitInfo initInfo)
        {
            _viewInitInfo = initInfo;
            _parentGameObject = initInfo.parentObj;
            this.gameObjectRoot = gameObjectRoot;
            CreateUISync();
        }

        public void CreateUISync()
        {
            Debug.Log("UISync Create");
            __SyncCreateInit();
        }

        public void CreateUIAsync() { }

        public void SetActive(bool active)
        {
            gameObjectRoot.SetActive(active);
        }

        public void __SyncCreateInit()
        {
            _viewInitInfo.AfterCallBack.Invoke(this);
        }

    }
}
