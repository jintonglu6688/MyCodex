using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerMcpToolApprovalMapper
    {
        internal const string QuestionId = "mycodex_mcp_approval";

        internal static bool IsToolApproval(IDictionary<string, object> parameters)
        {
            var meta = AppServerJsonValue.GetDictionary(parameters, "_meta");
            var schema = AppServerJsonValue.GetDictionary(parameters, "requestedSchema");
            object properties;
            return AppServerJsonValue.GetString(parameters, "mode") == "form" &&
                AppServerJsonValue.GetString(meta, "codex_approval_kind") == "mcp_tool_call" &&
                (!meta.ContainsKey("codex_request_type") || AppServerJsonValue.GetString(meta, "codex_request_type") == "approval_request") &&
                IsAbsentOrFalse(meta, "codex_requires_user_input") &&
                IsAbsentOrFalse(meta, "codex_strict_auto_review") &&
                AppServerJsonValue.GetString(schema, "type") == "object" &&
                schema.Keys.All(key => key == "type" || key == "properties") &&
                schema.TryGetValue("properties", out properties) &&
                properties is IDictionary<string, object> fields && fields.Count == 0;
        }

        internal static IReadOnlyList<AppServerToolUserInputQuestion> BuildChoices(IDictionary<string, object> parameters)
        {
            var question = new AppServerToolUserInputQuestion
            {
                Id = QuestionId,
                Question = SharedLocalization.Text("AppServerClient.McpApproval.Decision", "Approval choice"),
                Description = AppServerJsonValue.GetString(parameters, "message")
            };
            question.Options.Add(new AppServerToolUserInputOption
            {
                Value = "once", Label = SharedLocalization.Text("AppServerClient.McpApproval.Once", "Allow once")
            });
            if (SupportsPersist(parameters, "session")) question.Options.Add(new AppServerToolUserInputOption
            {
                Value = "session", Label = SharedLocalization.Text("AppServerClient.McpApproval.Session", "Allow for this session")
            });
            if (SupportsPersist(parameters, "always")) question.Options.Add(new AppServerToolUserInputOption
            {
                Value = "always", Label = IsComputerUseApplication(parameters)
                    ? SharedLocalization.Text("AppServerClient.McpApproval.AlwaysApp", "Always allow this app")
                    : SharedLocalization.Text("AppServerClient.McpApproval.Always", "Always allow this tool")
            });
            return new[] { question };
        }

        internal static IDictionary<string, object> BuildResponse(IDictionary<string, object> parameters,
            IDictionary<string, string[]> answers)
        {
            string[] values;
            var choice = answers != null && answers.TryGetValue(QuestionId, out values) && values != null && values.Length == 1
                ? values[0] : null;
            if (choice != "once" && ((choice != "session" && choice != "always") || !SupportsPersist(parameters, choice)))
                return AppServerInteractionRequestMapper.BuildMcpElicitationCancelResponse();

            var response = new Dictionary<string, object>
            {
                { "action", "accept" }, { "content", new Dictionary<string, object>() }
            };
            if (choice != "once") response["_meta"] = new Dictionary<string, object> { { "persist", choice } };
            return response;
        }

        private static bool SupportsPersist(IDictionary<string, object> parameters, string value)
        {
            var meta = AppServerJsonValue.GetDictionary(parameters, "_meta");
            object persist;
            if (!meta.TryGetValue("persist", out persist)) return false;
            if (persist is string text) return string.Equals(text, value, StringComparison.Ordinal);
            return persist is IList values && values.Cast<object>().Any(item => item is string scope && scope == value);
        }

        private static bool IsComputerUseApplication(IDictionary<string, object> parameters)
        {
            var meta = AppServerJsonValue.GetDictionary(parameters, "_meta");
            return string.Equals(AppServerJsonValue.GetString(meta, "connector_id"), "computer-use", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(AppServerJsonValue.GetString(AppServerJsonValue.GetDictionary(meta, "tool_params"), "app"));
        }

        private static bool IsAbsentOrFalse(IDictionary<string, object> meta, string key)
        {
            object value;
            return !meta.TryGetValue(key, out value) || (value is bool flag && !flag);
        }
    }
}
