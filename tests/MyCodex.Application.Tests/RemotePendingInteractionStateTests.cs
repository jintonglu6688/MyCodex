using System.Collections.Generic;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemotePendingInteractionStateTests
    {
        [TestCase(RemoteEventTypes.ApprovalRequested, RemoteEventTypes.ApprovalCompleted)]
        [TestCase(RemoteEventTypes.ToolInputRequested, RemoteEventTypes.ToolInputCompleted)]
        [TestCase(RemoteEventTypes.PlanExecutionRequested, RemoteEventTypes.PlanExecutionCompleted)]
        public void Observe_OnlyRetainsPendingRequestAndRemovesResolved(string requested, string completed)
        {
            var state = new RemotePendingInteractionState();
            var appEvent = new AppServerEventArgs("requested", "", threadId: "thread-a", turnId: "turn-a", requestId: "request-a");
            var remoteEvent = Event(requested, "request-a");
            state.Observe(appEvent, remoteEvent);
            state.Observe(new AppServerEventArgs("assistant", "history", threadId: "thread-a"), Event(RemoteEventTypes.CodingSessionUpdated, "history"));
            Assert.That(state.ListPending(), Has.Count.EqualTo(1));
            var snapshot = state.ListPending()[0];
            Assert.That(snapshot.EventType, Is.EqualTo(requested));
            Assert.That(snapshot.Payload["questionText"], Is.EqualTo("Existing question"));
            snapshot.Payload["questionText"] = "Mutated snapshot";
            Assert.That(state.ListPending()[0].Payload["questionText"], Is.EqualTo("Existing question"));

            state.Observe(new AppServerEventArgs("resolved", "", threadId: "thread-a", requestId: "request-a"), Event(completed, "request-a"));
            Assert.That(state.ListPending(), Is.Empty);
        }

        [TestCase("turn_completed")]
        [TestCase("turn_failed")]
        [TestCase("turn_interrupted")]
        public void EndedTurn_RemovesItsRequestsAndPreservesOtherTurn(string eventType)
        {
            var state = new RemotePendingInteractionState();
            state.Observe(new AppServerEventArgs("requested", "", threadId: "thread-a", turnId: "turn-a"), Event(RemoteEventTypes.ApprovalRequested, "a"));
            state.Observe(new AppServerEventArgs("requested", "", threadId: "thread-a", turnId: "turn-b"), Event(RemoteEventTypes.ToolInputRequested, "b"));
            state.Observe(new AppServerEventArgs(eventType, "", threadId: "thread-a", turnId: "turn-a"), null);
            Assert.That(state.ListPending(), Has.Count.EqualTo(1));
            Assert.That(state.ListPending()[0].RequestId, Is.EqualTo("b"));
            state.Observe(new AppServerEventArgs("app_server_disconnected", ""), null);
            Assert.That(state.ListPending(), Is.Empty);
        }

        [Test]
        public void ResolvedEvent_RemovesRequestEvenWhenSessionCanNoLongerBeMapped()
        {
            var state = new RemotePendingInteractionState();
            state.Observe(new AppServerEventArgs("approval_requested", "", threadId: "thread-a", requestId: "a"), Event(RemoteEventTypes.ApprovalRequested, "a"));
            state.Observe(new AppServerEventArgs("approval_resolved", "", threadId: "thread-a", requestId: "a"), null);
            Assert.That(state.ListPending(), Is.Empty);
        }

        [Test]
        public void SeparateTargetStates_DoNotShareRequests()
        {
            var a = new RemotePendingInteractionState();
            var b = new RemotePendingInteractionState();
            a.Observe(new AppServerEventArgs("requested", "", threadId: "same-thread"), Event(RemoteEventTypes.ApprovalRequested, "a"));
            Assert.That(b.ListPending(), Is.Empty);
            a.Clear();
            Assert.That(a.ListPending(), Is.Empty);
        }

        private static RemoteRelayEvent Event(string type, string request)
        {
            return new RemoteRelayEvent
            {
                EventType = type, RequestId = request, OperationId = "turn-a",
                Payload = new Dictionary<string, object> { { "questionText", "Existing question" }, { "requestId", request } }
            };
        }
    }
}
