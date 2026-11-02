using System;
using UnityEngine;
namespace Yato.Pooling
{
    [AddComponentMenu("")]
    public sealed class PoolMember : MonoBehaviour
    {
        internal Action Destroyed;
        private void OnDestroy() { var callback = Destroyed; Destroyed = null; callback?.Invoke(); }
    }
}
