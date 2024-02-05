using PureMVC.Interfaces;
using PureMVC.Patterns.Command;
using Unity.VisualScripting;
using UnityEngine;
using YatoUIFramework;

public class CreateUICommand : SimpleCommand
{
    public override void Execute(INotification notification)
    {
        Debug.Log("Start CreateUICommand");
        if (notification == null)
        {
            return;
        }
        UIBody body = (UIBody)notification.Body;
        if (body == null)
        {
            return;
        }
        _CreateView(body.pageInfo, body.uiParams);
    }

    public BaseUIView _CreateView(PageInfo data, object[] uiParams)
    {
        UIManager.Instance.CreateUI(data, uiParams, null, false, false);
        return null;
    }

    public void _BuildCell() { }

    public void _SetData() { }
    public void _FinshCreate() { }
}
