using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PureMVC.Patterns.Mediator;
using UnityEngine.UI;
using PureMVC.Interfaces;
using TMPro;

public class MyMediator : Mediator
{
    public const string mediatorName = "myMediator";
    //public Text txtNumber;
    public TMP_Text txtNum;
    public Button btnAdd;
    public Button btnSub;

    public MyMediator(GameObject root) : base(mediatorName)
    {
        //txtNumber = root.transform.Find("txtValue").GetComponent<Text>();
        txtNum = root.transform.Find("txtValue").GetComponent<TMP_Text>();
        btnAdd = root.transform.Find("Add").GetComponent<Button>();
        btnSub = root.transform.Find("Sub").GetComponent<Button>();

        btnAdd.onClick.AddListener(addBtn);
        btnSub.onClick.AddListener(subBtn);

    }
    /// <summary>
    /// 接受什么消息
    /// </summary>
    public override string[] ListNotificationInterests()
    {
        string[] list = new string[2];
        list[0] = "msg_add";
        list[1] = "msg_sub";
        return list;
    }
    /// <summary>
    /// 得到消息后执行什么
    /// </summary>
    /// <param name="notification"></param>
    public override void HandleNotification(INotification notification)
    {
        Debug.Log(notification.Name);
        switch (notification.Name)
        {
            case "msg_add":
                display(notification.Body as MyData);
                break;
            case "msg_sub":
                display(notification.Body as MyData);
                break;
            default:
                break;
        }
    }

    public void display(MyData myData)
    {
        //txtNumber.text = myData.dataValue.ToString();
        txtNum.text = myData.dataValue.ToString();
    }

    public void addBtn()
    {
        SendNotification("cmd_add");
    }

    public void subBtn()
    {
        SendNotification("cmd_sub");
    }
}