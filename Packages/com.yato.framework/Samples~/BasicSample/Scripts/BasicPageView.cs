using UnityEngine;
using Yato.UI;
namespace Yato.BasicSample
{
    public sealed class BasicPageView : UIPageView
    {
        public override void OnOpen(object args) { Debug.Log("[BasicSample] Page opened; prefab handle acquired"); }
        public override void OnClose(bool destroy) { Debug.Log("[BasicSample] Page closed; prefab handle released"); }
        private void OnGUI()
        {
            GUI.Box(new Rect(370, 20, 340, 100), "BasicSample: resource-backed UI page");
            if (GUI.Button(new Rect(390, 65, 280, 30), "Close page and release its resource")) Close();
        }
    }
}
