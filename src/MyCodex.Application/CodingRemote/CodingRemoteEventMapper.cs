using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.CodingRemote
{
    public static class CodingRemoteEventMapper
    {
        public static bool TryMap(AppServerEventArgs appEvent, ChatSession session, out RemoteRelayEvent remoteEvent)
        {
            remoteEvent = null;
            if (appEvent == null ||
                session == null ||
                string.IsNullOrWhiteSpace(session.Id) ||
                string.IsNullOrWhiteSpace(appEvent.ThreadId) ||
                !string.Equals(session.ThreadId, appEvent.ThreadId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var eventType = MapEventType(appEvent.EventType, session.Mode);
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return false;
            }

            remoteEvent = new RemoteRelayEvent
            {
                RequestId = appEvent.RequestId,
                OperationId = appEvent.TurnId,
                EventType = eventType,
                Payload = BuildPayload(appEvent, session, eventType)
            };
            return true;
        }

        private static string MapEventType(string eventType, AppInteractionMode mode)
        {
            switch ((eventType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "turn_started":
                case "assistant":
                    return mode == AppInteractionMode.Office
                        ? RemoteEventTypes.OfficeSessionUpdated
                        : RemoteEventTypes.CodingSessionUpdated;
                case "turn_completed":
                    return mode == AppInteractionMode.Office
                        ? RemoteEventTypes.OfficeMessageCompleted
                        : RemoteEventTypes.CodingMessageCompleted;
                case "turn_failed":
                case "error":
                case "turn_interrupted":
                    return mode == AppInteractionMode.Office
                        ? RemoteEventTypes.OfficeMessageFailed
                        : RemoteEventTypes.CodingMessageFailed;
                case "turn_rolled_back":
                case "thread_rewritten":
                case "thread_compacted":
                    return mode == AppInteractionMode.Office
                        ? RemoteEventTypes.OfficeSessionHistoryChanged
                        : RemoteEventTypes.CodingSessionHistoryChanged;
                case "approval_requested":
                    return RemoteEventTypes.ApprovalRequested;
                case "approval_resolved":
                    return RemoteEventTypes.ApprovalCompleted;
                default:
                    return null;
            }
        }

        private static Dictionary<string, object> BuildPayload(AppServerEventArgs appEvent, ChatSession session, string remoteEventType)
        {
            var payload = new Dictionary<string, object>
            {
                { "sessionId", session.Id },
                { "threadId", appEvent.ThreadId },
                { "turnId", appEvent.TurnId },
                { "status", MapStatus(remoteEventType) },
                { "isSubAgent", session.IsSubAgent }
            };

            if (!string.IsNullOrWhiteSpace(appEvent.RequestId))
            {
                payload["requestId"] = appEvent.RequestId;
            }

            return payload;
        }

        private static string MapStatus(string remoteEventType)
        {
            switch (remoteEventType)
            {
                case RemoteEventTypes.CodingMessageCompleted:
                case RemoteEventTypes.OfficeMessageCompleted:
                    return "completed";
                case RemoteEventTypes.CodingMessageFailed:
                case RemoteEventTypes.OfficeMessageFailed:
                    return "failed";
                case RemoteEventTypes.ApprovalRequested:
                    return "waiting_approval";
                case RemoteEventTypes.ApprovalCompleted:
                    return "approval_completed";
                default:
                    return "running";
            }
        }
    }
}
