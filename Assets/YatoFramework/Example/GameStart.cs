using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YatoUIFramework;

public class GameStart : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        ///临时写法
        UIManager.CreateInstance();
        ExampleDataManager.CreateInstance();
        PageInfo testPageInfo = new PageInfo();
        testPageInfo.name = "Example";
        testPageInfo.module = "YatoUIFramework";

        Object[] objs = new Object[2];
        UIBody body = new UIBody(testPageInfo, objs);
        UIManager.Instance.SendNotification("CreateUICommand", body);
    }

    // Update is called once per frame
    void Update()
    {
        UIManager.Instance.Update(Time.deltaTime);
    }
}
