using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace YatoUIFramework
{
    public partial class ExampleView : BaseUIView
    {
        public ExampleView(GameObject gameObjectRoot, ViewInitInfo initInfo) : base(gameObjectRoot, initInfo)
        {
            GetBindComponents(gameObjectRoot);
        }

        public void Init(ExampleViewData viewData)
        {
            m_TMP_Name.text = viewData.text_value;
        }

        public void Refresh(ExampleViewData viewData)
        {
            m_TMP_Name.text = viewData.text_value;
        }
    }
}
