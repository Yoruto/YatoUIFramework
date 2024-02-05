using PureMVC.Patterns.Proxy;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace YatoUIFramework
{

    public class BaseProxy : Proxy
    {
        public BaseProxy(string proxyName, object[] data = null) : base(proxyName, data)
        {
        }

        public override void OnRegister()
        {
            base.OnRegister();
            OnInit();
        }

        public virtual void Refresh()
        {

        }

        public virtual void OnInit()
        {

        }
    }
}
