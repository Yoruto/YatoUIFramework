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
            ///�����ѡ�񷢸����������߸����ݵײ�
            ExampleDataManager.Instance.TestValueChange();
        }
    }
}
