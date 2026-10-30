using System.Collections;

namespace Yato.Resources
{
    /// <summary>把 <see cref="IAwaitable"/> 包装成协程，便于与 Unity 协程流程混用。</summary>
    public static class AwaitableExtensions
    {
        public static IEnumerator AsEnumerator(this IAwaitable awaitable)
        {
            if (awaitable == null)
            {
                yield break;
            }

            while (!awaitable.IsDone)
            {
                yield return null;
            }
        }
    }
}
