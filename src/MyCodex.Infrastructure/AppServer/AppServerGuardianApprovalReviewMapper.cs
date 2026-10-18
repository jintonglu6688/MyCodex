using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerGuardianApprovalReviewMapper
    {
        internal static AppServerGuardianApprovalReviewEvent BuildStartedEvent(IDictionary<string, object> parameters)
        {
            return BuildEvent(parameters, false);
        }

        internal static AppServerGuardianApprovalReviewEvent BuildCompletedEvent(IDictionary<string, object> parameters)
        {
            return BuildEvent(parameters, true);
        }

        internal static AppServerGuardianApprovalReviewActionSummary BuildActionSummary(IDictionary<string, object> action)
        {
            var actionType = AppServerJsonValue.GetString(action, "type");
            switch (actionType)
            {
                case "command":
                    return new AppServerGuardianApprovalReviewActionSummary
                    {
                        ActionType = actionType,
                        Title = SharedLocalization.Text("AppServerGuardianApprovalReviewMapper.Action.Command", "Command"),
                        Details = JoinNonEmpty(AppServerJsonValue.GetString(action, "command"), AppServerJsonValue.GetString(action, "cwd"))
                    };
                case "execve":
                    return new AppServerGuardianApprovalReviewActionSummary
                    {
                        ActionType = actionType,
                        Title = SharedLocalization.Text("AppServerGuardianApprovalReviewMapper.Action.Execve", "Program execution"),
                        Details = JoinNonEmpty(AppServerJsonValue.GetString(action, "program"), JoinArray(action, "argv"), AppServerJsonValue.GetString(action, "cwd"))
                    };
                case "applyPatch":
                    return new AppServerGuardianApprovalReviewActionSummary
                    {
                        ActionType = actionType,
                        Title = SharedLocalization.Text("AppServerGuardianApprovalReviewMapper.Action.ApplyPatch", "Apply patch"),
                        Details = JoinNonEmpty(AppServerJsonValue.GetString(action, "cwd"), JoinArray(action, "files"))
                    };
                case "networkAccess":
                    return new AppServerGuardianApprovalReviewActionSummary
                    {
                        ActionType = actionType,
                        Title = SharedLocalization.Text("AppServerGuardianApprovalReviewMapper.Action.NetworkAccess", "Network access"),
                        Details = JoinNonEmpty(AppServerJsonValue.GetString(action, "target"), AppServerJsonValue.GetString(action, "host"), AppServerJsonValue.GetString(action, "protocol"), AppServerJsonValue.GetString(action, "port"))
                    };
                case "mcpToolCall":
                    return new AppServerGuardianApprovalReviewActionSummary
                    {
                        ActionType = actionType,
                        Title = SharedLocalization.Text("AppServerGuardianApprovalReviewMapper.Action.McpToolCall", "MCP tool call"),
                        Details = JoinNonEmpty(AppServerJsonValue.GetString(action, "server"), AppServerJsonValue.GetString(action, "toolTitle"), AppServerJsonValue.GetString(action, "toolName"))
                    };
                case "requestPermissions":
                    return new AppServerGuardianApprovalReviewActionSummary
                    {
                        ActionType = actionType,
                        Title = SharedLocalization.Text("AppServerGuardianApprovalReviewMapper.Action.RequestPermissions", "Permission request"),
                        Details = JoinNonEmpty(AppServerJsonValue.GetString(action, "reason"), SerializeValue(AppServerJsonValueReader.GetValue(action, "permissions")))
                    };
                default:
                    return new AppServerGuardianApprovalReviewActionSummary
                    {
                        ActionType = string.IsNullOrWhiteSpace(actionType) ? "unknown" : actionType,
                        Title = string.IsNullOrWhiteSpace(actionType)
                            ? SharedLocalization.Text("AppServerGuardianApprovalReviewMapper.Action.Unknown", "Guardian action")
                            : actionType,
                        Details = action == null ? string.Empty : SerializeValue(action)
                    };
            }
        }

        private static AppServerGuardianApprovalReviewEvent BuildEvent(IDictionary<string, object> parameters, bool completed)
        {
            var review = AppServerJsonValue.GetDictionary(parameters, "review");
            var result = new AppServerGuardianApprovalReviewEvent
            {
                ThreadId = AppServerJsonValue.GetString(parameters, "threadId"),
                TurnId = AppServerJsonValue.GetString(parameters, "turnId"),
                ReviewId = AppServerJsonValue.GetString(parameters, "reviewId"),
                TargetItemId = AppServerJsonValue.GetString(parameters, "targetItemId"),
                Status = AppServerJsonValue.GetString(review, "status"),
                RiskLevel = AppServerJsonValue.GetString(review, "riskLevel"),
                UserAuthorization = AppServerJsonValue.GetString(review, "userAuthorization"),
                Rationale = AppServerJsonValue.GetString(review, "rationale"),
                DecisionSource = AppServerJsonValue.GetString(parameters, "decisionSource"),
                StartedAtMs = GetNullableLong(parameters, "startedAtMs"),
                CompletedAtMs = GetNullableLong(parameters, "completedAtMs"),
                IsCompleted = completed,
                Action = BuildActionSummary(AppServerJsonValue.GetDictionary(parameters, "action"))
            };
            result.ApprovalRequest = AppServerGuardianDeniedActionMapper.BuildRequest(
                result, AppServerJsonValue.GetDictionary(parameters, "action"));
            return result;
        }

        private static long? GetNullableLong(IDictionary<string, object> source, string key)
        {
            object value;
            long result;
            return source != null &&
                   source.TryGetValue(key, out value) &&
                   value != null &&
                   long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? (long?)result
                : null;
        }

        private static string JoinArray(IDictionary<string, object> source, string key)
        {
            var parts = new List<string>();
            foreach (var item in GetArray(source, key))
            {
                if (item != null)
                {
                    parts.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
                }
            }

            return string.Join(" ", parts.ToArray());
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null || value is string)
            {
                yield break;
            }

            var enumerable = value as IEnumerable;
            if (enumerable == null)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                yield return item;
            }
        }

        private static string JoinNonEmpty(params string[] values)
        {
            var parts = new List<string>();
            if (values != null)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(values[i]))
                    {
                        parts.Add(values[i].Trim());
                    }
                }
            }

            return string.Join(" | ", parts.ToArray());
        }

        private static string SerializeValue(object value)
        {
            return value == null ? null : AppServerJsonValue.CreateSerializer().Serialize(value);
        }
    }
}
