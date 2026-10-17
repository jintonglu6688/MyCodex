using System;
using System.Collections;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerAgentActivityMapper
    {
        internal static AppServerThreadStatus MapThreadStatus(IDictionary<string, object> status)
        {
            var type = AppServerJsonValue.GetString(status, "type");
            return string.IsNullOrWhiteSpace(type) ? null : new AppServerThreadStatus
            {
                Type = type,
                ActiveFlags = ReadStrings(status, "activeFlags")
            };
        }

        internal static AppServerEventArgs Map(IDictionary<string, object> item, string threadId, string turnId, bool historical = false)
        {
            var type = AppServerJsonValue.GetString(item, "type");
            if (type == "subAgentActivity")
            {
                var activity = new AppServerSubAgentActivity
                {
                    AgentThreadId = AppServerJsonValue.GetString(item, "agentThreadId"),
                    AgentPath = AppServerJsonValue.GetString(item, "agentPath"),
                    Kind = AppServerJsonValue.GetString(item, "kind")
                };
                var agent = string.IsNullOrWhiteSpace(activity.AgentPath) ? activity.AgentThreadId : activity.AgentPath;
                return new AppServerEventArgs("subagent_activity",
                    string.Format(SharedLocalization.Text("AppServerClient.SubAgent.Activity.Template", "Subagent activity ({0}): {1}"), Abbreviate(activity.Kind), Abbreviate(agent)),
                    threadId: threadId, turnId: turnId, itemId: AppServerJsonValue.GetString(item, "id"),
                    reason: activity.Kind, details: agent, itemStatus: activity.Kind,
                    subAgentActivity: activity, isHistorical: historical);
            }
            if (type != "collabAgentToolCall") return null;

            var states = new Dictionary<string, AppServerCollabAgentState>(StringComparer.Ordinal);
            foreach (var pair in AppServerJsonValue.GetDictionary(item, "agentsStates"))
            {
                var state = pair.Value as IDictionary<string, object>;
                if (state == null) continue;
                states[pair.Key] = new AppServerCollabAgentState
                {
                    Status = AppServerJsonValue.GetString(state, "status"),
                    Message = AppServerJsonValue.GetString(state, "message")
                };
            }
            var collaboration = new AppServerCollaborationActivity
            {
                SenderThreadId = AppServerJsonValue.GetString(item, "senderThreadId"),
                ReceiverThreadIds = ReadStrings(item, "receiverThreadIds"),
                Tool = AppServerJsonValue.GetString(item, "tool"),
                Status = AppServerJsonValue.GetString(item, "status"),
                AgentsStates = states,
                Prompt = AppServerJsonValue.GetString(item, "prompt"),
                RequestedModel = AppServerJsonValue.GetString(item, "model"),
                RequestedReasoningEffort = AppServerJsonValue.GetString(item, "reasoningEffort")
            };
            return new AppServerEventArgs("collab_agent_tool_call", collaboration.Tool,
                threadId: threadId, turnId: turnId, itemId: AppServerJsonValue.GetString(item, "id"),
                itemStatus: collaboration.Status, collaboration: collaboration, isHistorical: historical);
        }

        private static string Abbreviate(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= 240 ? normalized : normalized.Substring(0, 237) + "...";
        }

        private static IReadOnlyList<string> ReadStrings(IDictionary<string, object> source, string key)
        {
            var result = new List<string>();
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value is string) return result;
            var values = value as IEnumerable;
            if (values == null) return result;
            foreach (var entry in values)
            {
                var text = entry as string;
                if (!string.IsNullOrWhiteSpace(text)) result.Add(text);
            }
            return result;
        }
    }
}
