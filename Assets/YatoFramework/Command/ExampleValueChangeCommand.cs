using PureMVC.Interfaces;
using PureMVC.Patterns.Command;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace YatoUIFramework
{
    public class ExampleValueChangeCommand : SimpleCommand
    {
        public override void Execute(INotification notification)
        {
            ///你可以选择发给服务器或者给数据底层
            ExampleDataManager.Instance.TestValueChange();
        }
    }
}
