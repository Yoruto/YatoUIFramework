using PureMVC.Interfaces;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace YatoUIFramework
{
    public class ExampleMediator : BaseMediator
    {
        public ExampleView viewComponent;
        public ExampleProxy proxy;

        public ExampleMediator(int uuid, string mediatorName, PageInfo pageInfo) : base(uuid, mediatorName, pageInfo)
        {
        }

        /// <summary>
        /// 本质来说不允许主动去proxy拿数据的，但是从开发角度来说允许这个操作(大概)
        /// </summary>
        public override void OnInit()
        {
            Debug.Log(name + " OnInit");
            if (GetViewComponent() == null)
            {
                Debug.LogError("cant find viewcomponent");
            }
            viewComponent = (ExampleView)GetViewComponent();
            proxy = (ExampleProxy)GetProxy();
            //绑定view按钮的方法，这么做是避免把一些不必要的东西告诉view
            viewComponent.AddButtonClickedHandler(ButtonClickedHandler);
            ///如果你不喜欢mediator和proxy之间用消息来传递，你可以试试用下面的方法，至少在开发的时候我经常那么做。。。
            /*
            proxy.InitData();
            var viewData =  proxy.GetData();
            viewComponent.Refresh(viewData);

            viewComponent.AddButtonClickedHandler(ButtonClickedHandler);
            */
        }

        /// <summary>
        /// 接受什么消息
        /// </summary>
        public override string[] ListNotificationInterests()
        {
            string[] list = {
            UINotificationType.TestValueChange,
            //如果你是通过持有的方式直接操作proxy，那就没必要去监听下面的消息，目前来说这只是一次性的消息。。。
            UINotificationType.TestValueInstantiated,
            };
            return list;
        }
        /// <summary>
        /// 得到消息后执行什么（其实可以优化）
        /// </summary>
        /// <param name="notification"></param>
        public override void HandleNotification(INotification notification)
        {
            switch (notification.Name)
            {
                case UINotificationType.TestValueChange:
                    TestValueChange((ExampleViewData)notification.Body);
                    break;
                case UINotificationType.TestValueInstantiated:
                    TestValueInstantiated((ExampleViewData)notification.Body);
                    break;
                default:
                    break;
            }
        }

        public void ButtonClickedHandler()
        {
            Debug.Log("test btn OnClick");
            //这里的消息是给服务器或者数据底层的,并不是直接给Proxy的！
            SendNotification("ValueChange");
        }

        public void TestValueChange(ExampleViewData body)
        {
            Debug.Log("refresh view data");
            //refaresh方法可以被动的让proxy去通知mediator，或者你也可以选择主动去
            // proxy.Refresh();
            proxy.InitData();
            viewComponent.Refresh(proxy.GetData());
        }

        public void TestValueInstantiated(ExampleViewData body)
        {
            Debug.Log("proxy Instantiated");
            viewComponent.Init(body);
        }
    }
}
