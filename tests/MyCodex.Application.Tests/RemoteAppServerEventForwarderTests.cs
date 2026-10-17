using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteAppServerEventForwarderTests
    {
        [Test]
        public async Task RecreatedForwarder_KeepsPendingInteractionUntilResolved()
        {
            var sink = new RecordingRemoteRealtimeEventSink();
            var session = new ChatSession { Id = "session-a", ThreadId = "thread-a", Mode = AppInteractionMode.Coding };
            var pending = new RemotePendingInteractionState();
            var first = new RemoteAppServerEventForwarder(threadId => session, sink, pendingInteractions: pending);
            await first.ForwardAsync(new AppServerEventArgs("approval_requested", "existing question",
                threadId: "thread-a", turnId: "turn-a", requestId: "approval-a"), CancellationToken.None);

            var reconnected = new RemoteAppServerEventForwarder(threadId => session, sink, pendingInteractions: pending);
            Assert.That(pending.ListPending(), Has.Count.EqualTo(1));
            await reconnected.ForwardAsync(new AppServerEventArgs("approval_resolved", "done",
                threadId: "thread-a", turnId: "turn-a", requestId: "approval-a"), CancellationToken.None);
            Assert.That(pending.ListPending(), Is.Empty);
        }

        [Test]
        public async Task ForwardAsync_MapsCodingThreadEventAndPublishesRemoteEvent()
        {
            var sink = new RecordingRemoteRealtimeEventSink();
            var session = new ChatSession
            {
                Id = "session-a",
                ThreadId = "thread-a",
                Mode = AppInteractionMode.Coding
            };
            var forwarder = new RemoteAppServerEventForwarder(threadId => session, sink);

            await forwarder.ForwardAsync(
                new AppServerEventArgs("turn_completed", "done", threadId: "thread-a", turnId: "turn-a"),
                CancellationToken.None);

            Assert.That(sink.Events, Has.Count.EqualTo(1));
            Assert.That(sink.Events[0].EventType, Is.EqualTo(RemoteEventTypes.CodingMessageCompleted));
            Assert.That(sink.Events[0].Payload["sessionId"], Is.EqualTo("session-a"));
        }

        [Test]
        public async Task ForwardAsync_IgnoresUnknownThread()
        {
            var sink = new RecordingRemoteRealtimeEventSink();
            var forwarder = new RemoteAppServerEventForwarder(threadId => null, sink);

            await forwarder.ForwardAsync(
                new AppServerEventArgs("turn_completed", "done", threadId: "missing"),
                CancellationToken.None);

            Assert.That(sink.Events, Is.Empty);
        }

        private sealed class RecordingRemoteRealtimeEventSink : IRemoteRealtimeEventSink
        {
            public RecordingRemoteRealtimeEventSink()
            {
                Events = new List<RemoteRelayEvent>();
            }

            public List<RemoteRelayEvent> Events { get; }

            public Task PublishAsync(RemoteRelayEvent remoteEvent, CancellationToken cancellationToken)
            {
                Events.Add(remoteEvent);
                return Task.CompletedTask;
            }
        }
    }
}
