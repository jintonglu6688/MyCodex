using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.CodingRemote
{
    public delegate bool TryMapRemoteAppServerEvent(
        AppServerEventArgs appEvent,
        ChatSession session,
        out RemoteRelayEvent remoteEvent);

    public sealed class RemoteAppServerEventForwarder
    {
        private readonly Func<string, ChatSession> _sessionResolver;
        private readonly IRemoteRealtimeEventSink _sink;
        private readonly TryMapRemoteAppServerEvent _mapper;
        private readonly IRemoteConversationSnapshotInvalidator _snapshotInvalidator;
        private readonly RemotePendingInteractionState _pendingInteractions;

        public RemoteAppServerEventForwarder(
            Func<string, ChatSession> sessionResolver,
            IRemoteRealtimeEventSink sink,
            TryMapRemoteAppServerEvent mapper = null,
            IRemoteConversationSnapshotInvalidator snapshotInvalidator = null,
            RemotePendingInteractionState pendingInteractions = null)
        {
            _sessionResolver = sessionResolver ?? throw new ArgumentNullException(nameof(sessionResolver));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _mapper = mapper ?? CodingRemoteEventMapper.TryMap;
            _snapshotInvalidator = snapshotInvalidator;
            _pendingInteractions = pendingInteractions;
        }

        public async Task ForwardAsync(AppServerEventArgs appEvent, CancellationToken cancellationToken)
        {
            if (appEvent == null || string.IsNullOrWhiteSpace(appEvent.ThreadId))
            {
                _pendingInteractions?.Observe(appEvent, null);
                return;
            }

            var session = _sessionResolver(appEvent.ThreadId);
            RemoteRelayEvent remoteEvent;
            if (!_mapper(appEvent, session, out remoteEvent))
            {
                _pendingInteractions?.Observe(appEvent, null);
                return;
            }

            _pendingInteractions?.Observe(appEvent, remoteEvent);

            if (_snapshotInvalidator != null && session != null)
            {
                if (remoteEvent.EventType == RemoteEventTypes.CodingSessionHistoryChanged || remoteEvent.EventType == RemoteEventTypes.OfficeSessionHistoryChanged)
                {
                    _snapshotInvalidator.MarkHistoryRewritten(session.Mode.ToString(), session.Id);
                }
                else
                {
                    _snapshotInvalidator.MarkTailChanged(session.Mode.ToString(), session.Id);
                }
            }

            await _sink.PublishAsync(remoteEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
