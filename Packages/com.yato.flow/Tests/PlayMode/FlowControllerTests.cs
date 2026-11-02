using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using Yato.App;
using Yato.Resources;

namespace Yato.Flow.Tests
{
    public sealed class FlowControllerTests
    {
        private sealed class State : IFlowState
        {
            public string Id { get; set; }
            public AwaitableCompletionSource Pending = new AwaitableCompletionSource();
            public FlowContext Context;
            public int Exits;
            public IAwaitable EnterAsync(FlowContext context, CancellationToken ct) { Context = context; return Pending; }
            public void Exit(FlowContext context) { Exits++; }
        }
        [UnityTest] public IEnumerator BusyCancellation_WaitsForLateWorkAndReleasesScope()
        {
            var state = new State { Id = "one" };
            using (var flow = new FlowController(new[] { state }))
            using (var cts = new CancellationTokenSource())
            {
                var operation = flow.SwitchAsync("one", cts.Token);
                yield return null; yield return null;
                Assert.AreEqual(FlowStatus.Busy, flow.SwitchAsync("one").GetResult().Status);
                cts.Cancel();
                yield return null;
                Assert.IsTrue(flow.IsBusy);
                state.Pending.SetResult();
                yield return operation.AsEnumerator();
                Assert.AreEqual(FlowStatus.Canceled, operation.GetResult().Status);
                Assert.IsNull(flow.CurrentStateId);
                Assert.IsTrue(state.Context.Resources.IsReleased);
                Assert.AreEqual(1, state.Exits);
            }
        }
        [UnityTest] public IEnumerator PreCanceled_LeavesCurrentStateUntouched()
        {
            var first = new State { Id = "first" }; first.Pending.SetResult();
            var second = new State { Id = "second" };
            using (var flow = new FlowController(new[] { first, second }))
            {
                yield return flow.SwitchAsync("first").AsEnumerator();
                using (var ct = new CancellationTokenSource())
                {
                    ct.Cancel();
                    var next = flow.SwitchAsync("second", ct.Token);
                    yield return next.AsEnumerator();
                    Assert.AreEqual(FlowStatus.Canceled, next.GetResult().Status);
                    Assert.AreEqual("first", flow.CurrentStateId);
                    Assert.AreEqual(0, first.Exits);
                }
            }
            Assert.AreEqual(1, first.Exits);
        }
        [UnityTest] public IEnumerator FailedEntry_CleansBothOldAndPartialNewState()
        {
            var first = new State { Id = "first" }; first.Pending.SetResult();
            var next = new State { Id = "next" }; next.Pending.SetException(new InvalidOperationException("entry failed"));
            using (var flow = new FlowController(new[] { first, next }))
            {
                yield return flow.SwitchAsync("first").AsEnumerator();
                var operation = flow.SwitchAsync("next");
                yield return operation.AsEnumerator();
                Assert.AreEqual(FlowStatus.Failed, operation.GetResult().Status);
                Assert.IsTrue(first.Context.Resources.IsReleased);
                Assert.IsTrue(next.Context.Resources.IsReleased);
                Assert.IsFalse(flow.IsBusy);
            }
            Assert.AreEqual(1, next.Exits);
        }
        [UnityTest] public IEnumerator DisposeDuringEntry_CancelsWithoutDroppingBusyEarly()
        {
            var state = new State { Id = "one" };
            var flow = new FlowController(new[] { state });
            var operation = flow.SwitchAsync("one");
            yield return null; yield return null;
            flow.Dispose();
            Assert.IsTrue(flow.IsBusy);
            state.Pending.SetResult();
            yield return operation.AsEnumerator();
            Assert.AreEqual(FlowStatus.Canceled, operation.GetResult().Status);
            Assert.IsTrue(state.Context.Resources.IsReleased);
            flow.Dispose();
        }
    }
}
