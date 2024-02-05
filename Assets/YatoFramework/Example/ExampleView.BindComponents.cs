using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using UnityEngine.Events;
namespace YatoUIFramework
{
    //自动生成于：2024/2/5 14:30:14
    public partial class ExampleView
    {

        private TextMeshProUGUI m_TMP_Name;
        private Image m_Img_Bg;
        private Button m_Btn_button;

        private void GetBindComponents(GameObject go)
        {
            ComponentAutoBindTool autoBindTool = go.GetComponent<ComponentAutoBindTool>();

            m_TMP_Name = autoBindTool.GetBindComponent<TextMeshProUGUI>(0);
            m_Img_Bg = autoBindTool.GetBindComponent<Image>(1);
            m_Btn_button = autoBindTool.GetBindComponent<Button>(2);
        }

        public void AddButtonClickedHandler(UnityAction func)
        {
            m_Btn_button.onClick.AddListener(func);
        }
    }
}
