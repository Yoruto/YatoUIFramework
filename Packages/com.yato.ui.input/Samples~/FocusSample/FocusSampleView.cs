using UnityEngine.UI;
namespace Yato.UI.Input.FocusSample
{
    public sealed class FocusSampleView : UIPageView
    {
        private FocusSampleArgs m_args;
        public override void OnCreate()
        {
            transform.Find("OpenPopup").GetComponent<Button>().onClick.AddListener(() => m_args?.Owner.Open(true));
            transform.Find("Close").GetComponent<Button>().onClick.AddListener(() => UIRouter.CloseAsync(Context.Handle));
        }
        public override void OnOpen(object args)
        {
            m_args = (FocusSampleArgs)args;
            transform.Find("Title").GetComponent<Text>().text = m_args.Popup ? "Popup: focus will return on close" : "Main page: mouse / arrows / gamepad";
            transform.Find("OpenPopup").gameObject.SetActive(!m_args.Popup);
        }
    }
}
