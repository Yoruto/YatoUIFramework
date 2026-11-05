using UnityEngine;

namespace Yato.Pooling.Tests
{
    public sealed class PoolCallbackProbe : MonoBehaviour, IPoolCallbacks
    {
        public static int Rents, Returns, UnpairedReturns;
        private bool m_rented;
        public void OnRent() { m_rented = true; Rents++; }
        public void OnReturn() { if (!m_rented) UnpairedReturns++; m_rented = false; Returns++; }
    }
}
