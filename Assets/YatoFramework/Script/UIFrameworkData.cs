using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace YatoUIFramework
{
    public class PageInfo
    {
        public string name;
        public string module;
        public Action afterCallBack;
    }

    public class ViewInitInfo
    {
        public GameObject parentObj;
        public Action<BaseUIView> AfterCallBack;

        public ViewInitInfo(GameObject parentObj, Action<BaseUIView> afterCallBack = null)
        {
            this.parentObj = parentObj;
            AfterCallBack = afterCallBack;
        }

    }

    public class UIBody
    {
        public PageInfo pageInfo;
        public object[] uiParams;

        public UIBody(PageInfo pageInfo, object[] uiParams = null)
        {
            this.pageInfo = pageInfo;
            this.uiParams = uiParams;
        }
    }

    public class ExampleViewData
    {
        public string text_value;
    }
}
