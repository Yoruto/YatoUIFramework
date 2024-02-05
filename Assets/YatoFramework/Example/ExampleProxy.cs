using UnityEngine;

namespace YatoUIFramework
{
    public class ExampleProxy : BaseProxy
    {
        public ExampleViewData _exampleViewData;

        public ExampleProxy(string proxyName = "", object[] data = null) : base(proxyName, data)
        {
            _exampleViewData = new ExampleViewData();

        }

        public override void OnInit()
        {
            base.OnInit();
            Debug.Log("proxy init");
            _exampleViewData.text_value = ExampleDataManager.Instance.GetTestValue().ToString();
            SendNotification(UINotificationType.TestValueInstantiated, _exampleViewData);
        }

        public override void Refresh()
        {
            base.Refresh();
            _exampleViewData.text_value = ExampleDataManager.Instance.GetTestValue().ToString();
            SendNotification(UINotificationType.TestValueInstantiated, _exampleViewData);
        }

        /// <summary>
        /// 如果你偷懒的话就直接从mediator调用这个方法去进行主动获取...
        /// </summary>
        public void InitData()
        {
            _exampleViewData.text_value = ExampleDataManager.Instance.GetTestValue().ToString();
        }

        public ExampleViewData GetData()
        {
            return _exampleViewData;
        }
    }
}
