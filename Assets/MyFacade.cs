using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PureMVC.Patterns.Facade;

public class MyFacade : Facade
{
    public MyFacade(GameObject root) : base()
    {
        RegisterCommand("cmd_add", () => { return new MyCommandAdd(); });
        RegisterCommand("cmd_sub", () => { return new MyCommandSub(); });

        RegisterMediator(new MyMediator(root));
        RegisterProxy(new MyDataProxy());
    }
}