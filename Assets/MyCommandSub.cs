using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PureMVC.Patterns.Command;
using PureMVC.Interfaces;

public class MyCommandSub : SimpleCommand
{

    public override void Execute(INotification notification)
    {
        MyDataProxy myDataProxy = Facade.RetrieveProxy("MyData01") as MyDataProxy;
        myDataProxy.subValue();
    }
}