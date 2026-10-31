using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using Yato.Resources;

namespace Yato.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UIPageTransition : MonoBehaviour
    {
        [Min(0)] public float EnterSeconds = 0.15f;
        [Min(0)] public float ExitSeconds = 0.15f;
        public event Action Changed;
        private CanvasGroup m_group;
        private int m_generation;
        private Yato.Resources.AwaitableCompletionSource m_current;
        private bool m_interactable;
        private bool m_blocks;
        private void Awake()
        {
            m_group = GetComponent<CanvasGroup>();
            m_interactable = m_group.interactable; m_blocks = m_group.blocksRaycasts;
        }
        private void OnEnable() { Play(true); }
        private void OnDisable() { Cancel(); }
        private void OnDestroy() { Cancel(); }
        public IAwaitable Play(bool entering, CancellationToken ct = default)
        {
            Cancel();
            if (m_group == null) Awake();
            var source = new Yato.Resources.AwaitableCompletionSource();
            m_current = source;
            int generation = ++m_generation;
            if (!isActiveAndEnabled) { source.SetException(new OperationCanceledException()); m_current = null; return source; }
            StartCoroutine(Fade(entering, ct, generation, source));
            return source;
        }
        public void Cancel()
        {
            m_generation++;
            var source = m_current; m_current = null;
            if (m_group != null)
            { m_group.alpha = 1; m_group.interactable = m_interactable; m_group.blocksRaycasts = m_blocks; }
            source?.SetException(new OperationCanceledException("UI transition canceled"));
            RaiseChanged();
        }
        private void RaiseChanged()
        {
            if (Changed == null) return;
            foreach (Action callback in Changed.GetInvocationList())
                try { callback(); } catch (Exception error) { Yato.Core.Log.Error("UITransition", "Observer failed", error); }
        }
        private IEnumerator Fade(bool entering, CancellationToken ct, int generation, Yato.Resources.AwaitableCompletionSource source)
        {
            float duration = entering ? EnterSeconds : ExitSeconds;
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0)
            { m_current = null; source.SetException(new ArgumentOutOfRangeException("Transition duration")); yield break; }
            float start = entering ? 0 : m_group.alpha;
            m_group.alpha = start;
            m_group.interactable = false; m_group.blocksRaycasts = false;
            float elapsed = 0;
            while (elapsed < duration)
            {
                if (generation != m_generation) yield break;
                if (ct.IsCancellationRequested || !isActiveAndEnabled) { Cancel(); yield break; }
                elapsed += Time.unscaledDeltaTime;
                m_group.alpha = Mathf.Lerp(start, entering ? 1 : 0, duration == 0 ? 1 : elapsed / duration);
                yield return null;
            }
            if (generation != m_generation) yield break;
            if (ct.IsCancellationRequested) { Cancel(); yield break; }
            m_group.alpha = entering ? 1 : 0;
            m_group.interactable = m_interactable; m_group.blocksRaycasts = m_blocks;
            m_current = null; source.SetResult(); RaiseChanged();
        }
    }
}
