using UnityEngine;
using Yato.UI;

namespace Yato
{
    public static partial class ModularConsumerVerification
    {
        static partial void AddOptionalUI(GameObject prefab) { prefab.AddComponent<UIPageView>(); }
    }
}
