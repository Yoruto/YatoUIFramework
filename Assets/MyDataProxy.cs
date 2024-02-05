using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PureMVC.Patterns.Proxy;

public class MyDataProxy : Proxy
{
    public const string proxyName = "MyData01";
    public MyData myData = null;
    public MyDataProxy() : base(proxyName)
    {
        myData = new MyData();
    }

    public void addValue()
    {
        myData.dataValue++;
        SendNotification("msg_add", myData);
    }

    public void subValue()
    {
        myData.dataValue--;
        SendNotification("msg_sub", myData);
    }
}