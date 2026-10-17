using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Application.CodingRemote
{
    public sealed class RemotePendingInteractionState
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, PendingEvent> _pending = new Dictionary<string, PendingEvent>(StringComparer.Ordinal);

        public void Observe(AppServerEventArgs appEvent, RemoteRelayEvent remoteEvent)
        {
            if (appEvent == null)
            {
                return;
            }

            lock (_sync)
            {
                if (string.Equals(appEvent.EventType, "app_server_disconnected", StringComparison.OrdinalIgnoreCase))
                {
                    _pending.Clear();
                    return;
                }

                var endedThread = string.Equals(appEvent.EventType, "thread_deleted", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(appEvent.EventType, "thread_archived", StringComparison.OrdinalIgnoreCase);
                var endedTurn = string.Equals(appEvent.EventType, "turn_completed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(appEvent.EventType, "turn_failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(appEvent.EventType, "turn_interrupted", StringComparison.OrdinalIgnoreCase);
                if ((endedThread || endedTurn) && !string.IsNullOrWhiteSpace(appEvent.ThreadId))
                {
                    foreach (var key in _pending.Where(pair =>
                        string.Equals(pair.Value.ThreadId, appEvent.ThreadId, StringComparison.OrdinalIgnoreCase) &&
                        (endedThread || string.IsNullOrWhiteSpace(appEvent.TurnId) ||
                         string.Equals(pair.Value.TurnId, appEvent.TurnId, StringComparison.Ordinal))).Select(pair => pair.Key).ToArray())
                    {
                        _pending.Remove(key);
                    }
                }

                if (remoteEvent == null)
                {
                    var completedType = string.Equals(appEvent.EventType, "approval_resolved", StringComparison.OrdinalIgnoreCase)
                        ? RemoteEventTypes.ApprovalRequested :
                        string.Equals(appEvent.EventType, "tool_user_input_resolved", StringComparison.OrdinalIgnoreCase)
                        ? RemoteEventTypes.ToolInputRequested :
                        string.Equals(appEvent.EventType, "plan_execution_completed", StringComparison.OrdinalIgnoreCase)
                        ? RemoteEventTypes.PlanExecutionRequested : null;
                    if (completedType != null && !string.IsNullOrWhiteSpace(appEvent.RequestId))
                    {
                        _pending.Remove(completedType + ":" + appEvent.RequestId);
                    }
                    return;
                }

                if (string.IsNullOrWhiteSpace(remoteEvent.RequestId))
                {
                    return;
                }

                var requestedType = RequestedType(remoteEvent.EventType);
                if (requestedType == null)
                {
                    return;
                }

                var requestKey = requestedType + ":" + remoteEvent.RequestId;
                if (!string.Equals(requestedType, remoteEvent.EventType, StringComparison.Ordinal))
                {
                    _pending.Remove(requestKey);
                    return;
                }

                _pending[requestKey] = new PendingEvent
                {
                    ThreadId = appEvent.ThreadId,
                    TurnId = appEvent.TurnId,
                    Json = RemoteRelayPayloadJsonSerializer.SerializeEvent(remoteEvent)
                };
            }
        }

        public IReadOnlyList<RemoteRelayEvent> ListPending()
        {
            lock (_sync)
            {
                return _pending.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => RemoteRelayPayloadJsonSerializer.DeserializeEvent(pair.Value.Json)).ToArray();
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _pending.Clear();
            }
        }

        private static string RequestedType(string eventType)
        {
            switch (eventType)
            {
                case RemoteEventTypes.ApprovalRequested:
                case RemoteEventTypes.ApprovalCompleted:
                    return RemoteEventTypes.ApprovalRequested;
                case RemoteEventTypes.ToolInputRequested:
                case RemoteEventTypes.ToolInputCompleted:
                    return RemoteEventTypes.ToolInputRequested;
                case RemoteEventTypes.PlanExecutionRequested:
                case RemoteEventTypes.PlanExecutionCompleted:
                    return RemoteEventTypes.PlanExecutionRequested;
                default:
                    return null;
            }
        }

        private sealed class PendingEvent
        {
            public string ThreadId { get; set; }
            public string TurnId { get; set; }
            public string Json { get; set; }
        }
    }
}
