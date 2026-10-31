using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Yato.App;
using Yato.Core;
using Yato.Resources;

namespace Yato.Flow
{
    public enum FlowStatus { Success, Busy, Canceled, Failed }
    public sealed class FlowResult
    {
        public FlowStatus Status { get; internal set; }
        public string StateId { get; internal set; }
        public Exception Error { get; internal set; }
    }
    public sealed class FlowContext
    {
        public string StateId { get; private set; }
        public ResourceScope Resources { get; private set; }
        internal FlowContext(string id) { StateId = id; Resources = new ResourceScope("flow/" + id); }
    }
    public interface IFlowState
    {
        string Id { get; }
        IAwaitable EnterAsync(FlowContext context, CancellationToken cancellationToken);
        void Exit(FlowContext context);
    }

    /// <summary>Main-thread flow transitions, above the synchronous Bootstrap contract.</summary>
    public sealed class FlowController : IDisposable
    {
        private readonly Dictionary<string, IFlowState> m_states = new Dictionary<string, IFlowState>(StringComparer.Ordinal);
        private IFlowState m_current;
        private FlowContext m_context;
        private CancellationTokenSource m_transition;
        private bool m_disposed;
        public bool IsBusy { get; private set; }
        public string CurrentStateId => m_current?.Id;
        public event Action<FlowResult> TransitionCompleted;

        public FlowController(IEnumerable<IFlowState> states)
        {
            if (states == null) throw new ArgumentNullException(nameof(states));
            foreach (var state in states)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.Id) || m_states.ContainsKey(state.Id))
                    throw new ArgumentException("Null state, empty ID or duplicate flow ID");
                m_states.Add(state.Id, state);
            }
        }

        public IAwaitable<FlowResult> SwitchAsync(string stateId, CancellationToken ct = default)
        {
            var source = new AwaitableCompletionSource<FlowResult>();
            if (m_disposed) { source.SetException(new ObjectDisposedException(nameof(FlowController))); return source; }
            if (IsBusy) { source.SetResult(new FlowResult { Status = FlowStatus.Busy, StateId = stateId }); return source; }
            if (stateId == null || !m_states.TryGetValue(stateId, out var state))
            {
                source.SetResult(new FlowResult { Status = FlowStatus.Failed, StateId = stateId, Error = new ArgumentException("Unknown flow state: " + stateId) });
                return source;
            }
            IsBusy = true;
            m_transition = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try { RuntimeHost.RunCoroutine(Guard(state, source, m_transition)); }
            catch (Exception error)
            {
                IsBusy = false; m_transition.Dispose(); m_transition = null;
                source.SetResult(new FlowResult { Status = FlowStatus.Failed, StateId = stateId, Error = error });
            }
            return source;
        }

        private IEnumerator Guard(IFlowState state, AwaitableCompletionSource<FlowResult> source, CancellationTokenSource cancellation)
        {
            var result = new FlowResult { Status = FlowStatus.Canceled, StateId = state.Id };
            var routine = Transition(state, cancellation, result);
            try
            {
                while (true)
                {
                    bool more;
                    try { more = routine.MoveNext(); }
                    catch (Exception error)
                    {
                        result.Error = error;
                        result.Status = error is OperationCanceledException ? FlowStatus.Canceled : FlowStatus.Failed;
                        break;
                    }
                    if (!more) break;
                    yield return routine.Current;
                }
            }
            finally
            {
                (routine as IDisposable)?.Dispose();
                source.SetResult(result);
                Raise(result);
            }
        }

        private IEnumerator Transition(IFlowState state, CancellationTokenSource cancellation, FlowResult result)
        {
            var entering = new FlowContext(state.Id);
            bool began = false, installed = false;
            try
            {
                yield return null; // Explicit pre-transition cancellation window.
                cancellation.Token.ThrowIfCancellationRequested();
                var old = m_current; var oldContext = m_context;
                m_current = null; m_context = null;
                if (old != null)
                {
                    try { old.Exit(oldContext); }
                    finally { oldContext.Resources.Dispose(); }
                }
                began = true;
                IAwaitable operation = null;
                Exception failure = null;
                try
                {
                    operation = state.EnterAsync(entering, cancellation.Token);
                    if (operation == null) throw new InvalidOperationException("EnterAsync returned null");
                }
                catch (Exception error) { failure = error; }
                if (failure == null)
                {
                    while (!operation.IsDone) yield return null; // Do not release Busy while canceled work is still running.
                    try { operation.GetResult(); cancellation.Token.ThrowIfCancellationRequested(); }
                    catch (Exception error) { failure = error; }
                }
                if (failure != null)
                {
                    result.Error = failure;
                    result.Status = failure is OperationCanceledException ? FlowStatus.Canceled : FlowStatus.Failed;
                }
                else
                {
                    m_current = state; m_context = entering; installed = true;
                    result.Status = FlowStatus.Success;
                }
            }
            finally
            {
                if (!installed)
                {
                    if (began) SafeExit(state, entering);
                    else entering.Resources.Dispose();
                }
                if (m_disposed && installed) ReleaseCurrent();
                cancellation.Dispose();
                m_transition = null;
                IsBusy = false;
            }
        }

        private void Raise(FlowResult result)
        {
            if (TransitionCompleted == null) return;
            foreach (Action<FlowResult> callback in TransitionCompleted.GetInvocationList())
                try { callback(result); } catch (Exception error) { Log.Error("Flow", "Transition observer failed", error); }
        }
        private static void SafeExit(IFlowState state, FlowContext context)
        {
            try { state.Exit(context); } catch (Exception error) { Log.Error("Flow", "State exit failed", error); }
            finally { context.Resources.Dispose(); }
        }
        private void ReleaseCurrent()
        {
            var state = m_current; var context = m_context;
            m_current = null; m_context = null;
            if (state != null) SafeExit(state, context);
        }
        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            TransitionCompleted = null;
            m_transition?.Cancel();
            if (!IsBusy) ReleaseCurrent();
        }
    }
}
