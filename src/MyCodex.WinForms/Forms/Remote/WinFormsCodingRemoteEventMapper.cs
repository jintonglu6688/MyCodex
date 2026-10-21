using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class WinFormsCodingRemoteEventMapper
    {
        internal static bool TryMap(AppServerEventArgs appEvent, ChatSession session, out RemoteRelayEvent remoteEvent)
        {
            if (TryMapPlanExecutionRequested(appEvent, session, out remoteEvent))
            {
                return true;
            }

            if (TryMapPlanExecutionCompleted(appEvent, session, out remoteEvent))
            {
                return true;
            }

            if (TryMapToolInputRequested(appEvent, session, out remoteEvent))
            {
                return true;
            }

            if (TryMapToolInputCompleted(appEvent, session, out remoteEvent))
            {
                return true;
            }

            if (TryMapAsyncInputQueued(appEvent, session, out remoteEvent))
            {
                return true;
            }

            if (TryMapAsyncInputResolved(appEvent, session, out remoteEvent))
            {
                return true;
            }

            if (TryMapApprovalRequested(appEvent, session, out remoteEvent))
            {
                return true;
            }

            return CodingRemoteEventMapper.TryMap(appEvent, session, out remoteEvent);
        }

        private static bool IsRemoteThreadEvent(AppServerEventArgs appEvent, ChatSession session, string eventType)
        {
            return appEvent != null &&
                session != null &&
                !string.IsNullOrWhiteSpace(session.Id) &&
                !string.IsNullOrWhiteSpace(appEvent.ThreadId) &&
                string.Equals(session.ThreadId, appEvent.ThreadId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(appEvent.EventType, eventType, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryMapApprovalRequested(AppServerEventArgs appEvent, ChatSession session, out RemoteRelayEvent remoteEvent)
        {
            remoteEvent = null;
            if (!IsRemoteThreadEvent(appEvent, session, "approval_requested"))
            {
                return false;
            }

            var summary = ApprovalRequestSummaryBuilder.Build(appEvent);
            remoteEvent = new RemoteRelayEvent
            {
                RequestId = appEvent.RequestId,
                OperationId = appEvent.TurnId,
                EventType = RemoteEventTypes.ApprovalRequested,
                Payload = new Dictionary<string, object>
                {
                    { "sessionId", session.Id },
                    { "threadId", appEvent.ThreadId },
                    { "turnId", appEvent.TurnId },
                    { "requestId", appEvent.RequestId },
                    { "status", "waiting_approval" },
                    { "approvalKind", FormatApprovalKind(appEvent.ApprovalKind) },
                    { "canApproveForSession", appEvent.CanApproveForSession },
                    { "title", summary.WindowTitle },
                    { "questionText", summary.QuestionText },
                    { "summaryText", summary.DialogSummary },
                    { "detailsText", summary.DetailsText },
                    { "actionKey", summary.ActionKey.ToString() },
                    { "decisions", BuildDecisions(appEvent.CanApproveForSession) }
                }
            };
            return true;
        }

        private static bool TryMapPlanExecutionRequested(AppServerEventArgs appEvent, ChatSession session, out RemoteRelayEvent remoteEvent)
        {
            remoteEvent = null;
            if (!IsRemoteThreadEvent(appEvent, session, "plan_execution_requested") ||
                string.IsNullOrWhiteSpace(appEvent.RequestId))
            {
                return false;
            }

            remoteEvent = new RemoteRelayEvent
            {
                RequestId = appEvent.RequestId,
                OperationId = appEvent.TurnId,
                EventType = RemoteEventTypes.PlanExecutionRequested,
                Payload = new Dictionary<string, object>
                {
                    { "sessionId", session.Id },
                    { "threadId", appEvent.ThreadId },
                    { "turnId", appEvent.TurnId },
                    { "requestId", appEvent.RequestId },
                    { "status", "waiting_plan_decision" },
                    { "title", UiLocalization.Text("PlanExecutionPromptForm.Title", "Plan Ready") },
                    { "questionText", UiLocalization.Text("PlanExecutionPromptForm.Prompt", "Review the generated plan below, then choose what to do next.") },
                    { "planText", appEvent.Content },
                    { "actions", BuildPlanExecutionActions() }
                }
            };
            return true;
        }

        private static bool TryMapPlanExecutionCompleted(AppServerEventArgs appEvent, ChatSession session, out RemoteRelayEvent remoteEvent)
        {
            remoteEvent = null;
            if (!IsRemoteThreadEvent(appEvent, session, "plan_execution_completed") ||
                string.IsNullOrWhiteSpace(appEvent.RequestId))
            {
                return false;
            }

            remoteEvent = new RemoteRelayEvent
            {
                RequestId = appEvent.RequestId,
                OperationId = appEvent.TurnId,
                EventType = RemoteEventTypes.PlanExecutionCompleted,
                Payload = new Dictionary<string, object>
                {
                    { "sessionId", session.Id },
                    { "threadId", appEvent.ThreadId },
                    { "turnId", appEvent.TurnId },
                    { "requestId", appEvent.RequestId },
                    { "status", "plan_decision_completed" },
                    { "action", appEvent.Details },
                    { "message", appEvent.Content }
                }
            };
            return true;
        }

        private static bool TryMapToolInputRequested(AppServerEventArgs appEvent, ChatSession session, out RemoteRelayEvent remoteEvent)
        {
            remoteEvent = null;
            if (!IsRemoteThreadEvent(appEvent, session, "tool_user_input_requested") ||
                string.IsNullOrWhiteSpace(appEvent.RequestId) ||
                appEvent.ToolQuestions == null ||
                appEvent.ToolQuestions.Count == 0 ||
                !string.IsNullOrWhiteSpace(appEvent.McpServerName))
            {
                return false;
            }

            remoteEvent = new RemoteRelayEvent
            {
                RequestId = appEvent.RequestId,
                OperationId = appEvent.TurnId,
                EventType = RemoteEventTypes.ToolInputRequested,
                Payload = new Dictionary<string, object>
                {
                    { "sessionId", session.Id },
                    { "threadId", appEvent.ThreadId },
                    { "turnId", appEvent.TurnId },
                    { "itemId", appEvent.ItemId },
                    { "requestId", appEvent.RequestId },
                    { "status", "waiting_input" },
                    { "title", UiLocalization.Text("ToolUserInputForm.Title", "Plan Mode") },
                    { "headerText", UiLocalization.Text("ToolUserInputForm.Header", "The tool needs additional input before it can continue.") },
                    { "detailsText", appEvent.Content },
                    { "questions", BuildToolInputQuestions(appEvent.ToolQuestions, false) }
                }
            };
            return true;
        }

        private static bool TryMapToolInputCompleted(AppServerEventArgs appEvent, ChatSession session, out RemoteRelayEvent remoteEvent)
        {
            remoteEvent = null;
            if (!IsRemoteThreadEvent(appEvent, session, "tool_user_input_resolved") ||
                string.IsNullOrWhiteSpace(appEvent.RequestId))
            {
                return false;
            }

            remoteEvent = new RemoteRelayEvent
            {
                RequestId = appEvent.RequestId,
                OperationId = appEvent.TurnId,
                EventType = RemoteEventTypes.ToolInputCompleted,
                Payload = new Dictionary<string, object>
                {
                    { "sessionId", session.Id },
                    { "threadId", appEvent.ThreadId },
                    { "turnId", appEvent.TurnId },
                    { "requestId", appEvent.RequestId },
                    { "status", "input_completed" },
                    { "message", appEvent.Content }
                }
            };
            return true;
        }

        private static bool TryMapAsyncInputQueued(AppServerEventArgs appEvent, ChatSession session, out RemoteRelayEvent remoteEvent)
        {
            remoteEvent = null;
            if (!IsRemoteThreadEvent(appEvent, session, RemoteAsyncInputRequests.QueuedEventType) ||
                string.IsNullOrWhiteSpace(appEvent.RequestId) ||
                appEvent.ToolQuestions == null ||
                appEvent.ToolQuestions.Count == 0)
            {
                return false;
            }

            remoteEvent = new RemoteRelayEvent
            {
                RequestId = appEvent.RequestId,
                OperationId = appEvent.TurnId,
                EventType = RemoteEventTypes.ToolInputRequested,
                Payload = new Dictionary<string, object>
                {
                    { "sessionId", session.Id },
                    { "threadId", appEvent.ThreadId },
                    { "turnId", appEvent.TurnId },
                    { "itemId", appEvent.ItemId },
                    { "requestId", appEvent.RequestId },
                    { "kind", RemoteAsyncInputRequests.Kind },
                    { "status", "waiting_input" },
                    { "title", UiLocalization.Text("AsyncInputForm.Title", "Pending questions") },
                    {
                        "headerText",
                        string.IsNullOrWhiteSpace(appEvent.Details)
                            ? null
                            : string.Format(CultureInfo.CurrentCulture, UiLocalization.Text("AsyncInputForm.Source.Template", "Source: {0}"), appEvent.Details)
                    },
                    // The Windows window always offers a free-form answer next to the suggested options.
                    { "questions", BuildToolInputQuestions(appEvent.ToolQuestions, true) }
                }
            };
            return true;
        }

        private static bool TryMapAsyncInputResolved(AppServerEventArgs appEvent, ChatSession session, out RemoteRelayEvent remoteEvent)
        {
            remoteEvent = null;
            if (!IsRemoteThreadEvent(appEvent, session, RemoteAsyncInputRequests.ResolvedEventType) ||
                string.IsNullOrWhiteSpace(appEvent.RequestId))
            {
                return false;
            }

            remoteEvent = new RemoteRelayEvent
            {
                RequestId = appEvent.RequestId,
                OperationId = appEvent.TurnId,
                EventType = RemoteEventTypes.ToolInputCompleted,
                Payload = new Dictionary<string, object>
                {
                    { "sessionId", session.Id },
                    { "threadId", appEvent.ThreadId },
                    { "turnId", appEvent.TurnId },
                    { "requestId", appEvent.RequestId },
                    { "kind", RemoteAsyncInputRequests.Kind },
                    { "status", "input_completed" }
                }
            };
            return true;
        }

        private static IReadOnlyList<Dictionary<string, object>> BuildToolInputQuestions(IReadOnlyList<AppServerToolUserInputQuestion> questions, bool offerOtherWithOptions)
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var question in questions)
            {
                if (question == null || string.IsNullOrWhiteSpace(question.Id))
                {
                    continue;
                }

                result.Add(new Dictionary<string, object>
                {
                    { "id", question.Id },
                    { "header", question.Header },
                    { "question", question.Question },
                    { "description", question.Description },
                    { "isOther", question.IsOther || (offerOtherWithOptions && question.Options != null && question.Options.Count > 0) },
                    { "isSecret", question.IsSecret },
                    { "options", BuildToolInputOptions(question.Options) }
                });
            }

            return result;
        }

        private static IReadOnlyList<Dictionary<string, object>> BuildToolInputOptions(IReadOnlyList<AppServerToolUserInputOption> options)
        {
            var result = new List<Dictionary<string, object>>();
            if (options == null)
            {
                return result;
            }

            foreach (var option in options)
            {
                if (option == null)
                {
                    continue;
                }

                result.Add(new Dictionary<string, object>
                {
                    { "label", option.Label },
                    { "value", string.IsNullOrWhiteSpace(option.Value) ? option.Label : option.Value },
                    { "description", option.Description }
                });
            }

            return result;
        }

        private static IReadOnlyList<Dictionary<string, object>> BuildPlanExecutionActions()
        {
            return new List<Dictionary<string, object>>
            {
                PlanExecutionAction("execute", UiLocalization.Text("PlanExecutionPromptForm.Option.Execute", "Execute Plan(&E)")),
                PlanExecutionAction("continue", UiLocalization.Text("PlanExecutionPromptForm.Option.ContinuePlanning", "Continue Discussion(&C)"))
            };
        }

        private static Dictionary<string, object> PlanExecutionAction(string id, string label)
        {
            return new Dictionary<string, object>
            {
                { "id", id },
                { "label", label },
                { "enabled", true }
            };
        }

        private static string FormatApprovalKind(AppServerApprovalRequestKind? kind)
        {
            return kind.HasValue ? kind.Value.ToString() : string.Empty;
        }

        private static IReadOnlyList<Dictionary<string, object>> BuildDecisions(bool canApproveForSession)
        {
            var decisions = new List<Dictionary<string, object>>
            {
                Decision("allow_once", UiLocalization.Text("CodingRemote.Approval.Decision.AllowOnce", "Allow once"), true)
            };
            if (canApproveForSession)
            {
                decisions.Add(Decision("allow_for_session", UiLocalization.Text("CodingRemote.Approval.Decision.AllowForSession", "Allow for session"), true));
            }

            decisions.Add(Decision("decline", UiLocalization.Text("CodingRemote.Approval.Decision.Decline", "Decline"), true));
            return decisions;
        }

        private static Dictionary<string, object> Decision(string id, string label, bool enabled)
        {
            return new Dictionary<string, object>
            {
                { "id", id },
                { "label", label },
                { "enabled", enabled }
            };
        }
    }
}
