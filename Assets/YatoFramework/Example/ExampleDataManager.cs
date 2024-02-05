using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace YatoUIFramework
{
    public class ExampleDataManager : Singleton<ExampleDataManager>
    {
        private int _textValue;

        public void TestValueChange()
        {
            Debug.Log("database change value");
            _textValue++;
            //你可以选择发一个body给过去或者不发让其他模块去主动获取
            UIManager.Instance.SendNotification(UINotificationType.TestValueChange);
        }

        public int GetTestValue()
        {
            return _textValue;
        }
    }
}
