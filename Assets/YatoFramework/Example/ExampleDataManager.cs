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
            //�����ѡ��һ��body����ȥ���߲���������ģ��ȥ������ȡ
            UIManager.Instance.SendNotification(UINotificationType.TestValueChange);
        }

        public int GetTestValue()
        {
            return _textValue;
        }
    }
}
