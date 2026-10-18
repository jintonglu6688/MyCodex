using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerNotificationFactory
    {
        internal static AppServerEventArgs CreateThreadStarted(string threadId, AppServerThreadSummary summary = null)
        {
            return new AppServerEventArgs(
                "thread_started",
                L("AppServerClient.Thread.Started", "Thread is ready."),
                threadId: threadId, threadSummary: summary, threadStatus: summary?.ThreadStatus);
        }

        internal static AppServerEventArgs CreateThreadArchived(string threadId)
        {
            return new AppServerEventArgs(
                "thread_archived",
                L("AppServerClient.Thread.Archived", "Session archived."),
                threadId: threadId);
        }

        internal static AppServerEventArgs CreateThreadDeleted(string threadId)
        {
            return new AppServerEventArgs(
                "thread_deleted",
                L("AppServerClient.Thread.Deleted", "Session deleted."),
                threadId: threadId);
        }

        internal static AppServerEventArgs CreateThreadClosed(string threadId)
        {
            return new AppServerEventArgs(
                "thread_closed",
                L("AppServerClient.Thread.Closed", "Session closed."),
                threadId: threadId);
        }

        internal static AppServerEventArgs CreateThreadUnarchived(string threadId)
        {
            return new AppServerEventArgs(
                "thread_unarchived",
                L("AppServerClient.Thread.Unarchived", "Session restored from archive."),
                threadId: threadId);
        }

        internal static AppServerEventArgs CreateThreadCompacted(string threadId, string turnId)
        {
            return new AppServerEventArgs(
                "context_compaction_completed",
                L("AppServerClient.Thread.ContextCompacted", "Session context compressed."),
                threadId: threadId,
                turnId: turnId);
        }

        internal static AppServerEventArgs CreateThreadStatusChanged(string threadId, string statusType, AppServerThreadStatus status = null)
        {
            return new AppServerEventArgs(
                "thread_status_changed",
                L("AppServerClient.Thread.StatusUpdated.Prefix", "Thread status updated: ") + statusType,
                threadId: threadId, threadStatus: status ?? new AppServerThreadStatus { Type = statusType });
        }

        internal static AppServerEventArgs CreateTurnStarted(string threadId, string turnId)
        {
            return new AppServerEventArgs(
                "turn_started",
                L("AppServerClient.Turn.Started", "Codex is processing..."),
                threadId: threadId,
                turnId: turnId);
        }

        internal static AppServerEventArgs CreateTurnFailed(
            string threadId,
            string turnId,
            string errorMessage,
            string reason = null)
        {
            return new AppServerEventArgs(
                "turn_failed",
                ResolveErrorMessage(
                    errorMessage,
                    reason,
                    "AppServerClient.Error.TurnRequestFailed",
                    "Current turn request failed."),
                threadId: threadId,
                turnId: turnId,
                reason: reason);
        }

        internal static AppServerEventArgs CreateTurnInterrupted(string threadId, string turnId)
        {
            return new AppServerEventArgs(
                "turn_interrupted",
                L("AppServerClient.Turn.Interrupted", "Current response interrupted."),
                threadId,
                turnId);
        }

        internal static AppServerEventArgs CreateTurnCompleted(string threadId, string turnId)
        {
            return new AppServerEventArgs(
                "turn_completed",
                L("AppServerClient.Turn.Completed", "Current turn request completed."),
                threadId,
                turnId);
        }

        internal static AppServerEventArgs CreateError(
            string threadId,
            string turnId,
            string errorMessage,
            string reason = null)
        {
            return new AppServerEventArgs(
                "error",
                ResolveErrorMessage(
                    errorMessage,
                    reason,
                    "AppServerClient.Error.ServerReturnedError",
                    "App Server returned an error."),
                threadId: threadId,
                turnId: turnId,
                reason: reason);
        }

        private static string ResolveErrorMessage(
            string errorMessage,
            string reason,
            string fallbackKey,
            string fallback)
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                return errorMessage;
            }

            return string.Equals(reason, "sessionBudgetExceeded", StringComparison.Ordinal)
                ? L(
                    "AppServerClient.Error.SessionBudgetExceeded",
                    "The current session has reached its budget limit.")
                : L(fallbackKey, fallback);
        }

        internal static AppServerEventArgs CreateAuthChanged(string authMode, string planType)
        {
            var message = string.IsNullOrWhiteSpace(authMode)
                ? L("AppServerClient.Account.StatusUpdated", "Account status updated.")
                : LF(
                    "AppServerClient.Account.StatusUpdatedWithMode.Template",
                    "Account status updated: {0}{1}",
                    authMode,
                    string.IsNullOrWhiteSpace(planType) ? string.Empty : " / " + planType);
            return new AppServerEventArgs("auth_changed", message);
        }

        internal static AppServerEventArgs CreateAuthLoginCompleted(bool success, string message, string requestId)
        {
            return new AppServerEventArgs(
                success ? "auth_login_completed" : "error",
                success
                    ? L("AppServerClient.Account.LoginCompleted", "Login completed.")
                    : (string.IsNullOrWhiteSpace(message)
                        ? L("AppServerClient.Account.LoginFailed", "Login failed.")
                        : message),
                requestId: requestId);
        }

        internal static AppServerEventArgs CreateMcpOauthLoginCompleted(string serverName, bool? success, string errorMessage)
        {
            var displayName = string.IsNullOrWhiteSpace(serverName)
                ? L("AppServerClient.Value.UnknownServer", "(Unknown server)")
                : serverName;
            var message = success.HasValue
                ? (success.Value
                    ? LF(
                        "AppServerClient.Mcp.LoginCompleted.Template",
                        "MCP server login completed: {0}",
                        displayName)
                    : (!string.IsNullOrWhiteSpace(errorMessage)
                        ? errorMessage
                        : LF(
                            "AppServerClient.Mcp.LoginFailed.Template",
                            "MCP server login failed: {0}",
                            displayName)))
                : LF(
                    "AppServerClient.Mcp.LoginStatusUpdated.Template",
                    "MCP server login status updated: {0}",
                    displayName);
            return new AppServerEventArgs(
                success.HasValue && !success.Value ? "error" : "mcp_oauth_login_completed",
                message,
                requestId: serverName);
        }

        internal static AppServerEventArgs CreateMcpServerStartupStatusUpdated(IDictionary<string, object> parameters)
        {
            var serverName = AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "serverName", "name", "id");
            var status = ExtractStatusText(parameters);
            var message = ExtractRuntimeMessage(parameters);
            var failureReason = ExtractMcpStartupFailureReason(parameters);
            var displayName = string.IsNullOrWhiteSpace(serverName)
                ? L("AppServerClient.Value.UnknownServer", "(Unknown server)")
                : serverName;
            var statusText = string.IsNullOrWhiteSpace(status)
                ? L("AppServerClient.Mcp.StartupStatus.Unknown", "updated")
                : status;
            var content = string.IsNullOrWhiteSpace(message)
                ? LF(
                    "AppServerClient.Mcp.StartupStatusUpdated.Template",
                    "MCP server startup status updated: {0}, {1}.",
                    displayName,
                    statusText)
                : LF(
                    "AppServerClient.Mcp.StartupStatusUpdatedWithMessage.Template",
                    "MCP server startup status updated: {0}, {1}. {2}",
                    displayName,
                    statusText,
                    message);

            if (string.Equals(failureReason, "reauthenticationRequired", StringComparison.Ordinal))
            {
                var safeMessage = GetSafeMcpReauthenticationMessage(message);
                return new AppServerEventArgs(
                    "mcp_startup_status_updated",
                    string.IsNullOrWhiteSpace(safeMessage)
                        ? LF(
                            "AppServerClient.Mcp.ReauthenticationRequired.Template",
                            "MCP server authentication expired: {0}.",
                            displayName)
                        : LF(
                            "AppServerClient.Mcp.StartupStatusUpdatedWithMessage.Template",
                            "MCP server startup status updated: {0}, {1}. {2}",
                            displayName,
                            statusText,
                            safeMessage),
                    threadId: AppServerJsonValueReader.GetString(parameters, "threadId"),
                    turnId: AppServerJsonValueReader.GetString(parameters, "turnId"),
                    details: "mcpServer/startupStatus/updated",
                    reason: failureReason,
                    mcpServerName: serverName,
                    itemStatus: status);
            }

            if (IsWarningStatus(status, message))
            {
                return CreateProtocolWarning(content, parameters, "mcpServer/startupStatus/updated");
            }

            return new AppServerEventArgs(
                "mcp_startup_status_updated",
                content,
                threadId: AppServerJsonValueReader.GetString(parameters, "threadId"),
                turnId: AppServerJsonValueReader.GetString(parameters, "turnId"),
                details: "mcpServer/startupStatus/updated",
                mcpServerName: serverName,
                itemStatus: status);
        }

        internal static AppServerEventArgs CreateRateLimitsChanged()
        {
            return new AppServerEventArgs(
                "rate_limits_changed",
                L("AppServerClient.RateLimits.Updated", "Quota information updated."));
        }

        internal static AppServerEventArgs CreateTokenUsage(
            string threadId,
            string turnId,
            long? totalTokens,
            long? modelContextWindow)
        {
            var usage = new AppServerThreadTokenUsageSnapshot
            {
                ModelContextWindow = modelContextWindow
            };
            usage.Total.TotalTokens = totalTokens.GetValueOrDefault();
            return CreateTokenUsage(threadId, turnId, usage);
        }

        internal static AppServerEventArgs CreateTokenUsage(
            string threadId,
            string turnId,
            AppServerThreadTokenUsageSnapshot tokenUsage)
        {
            return new AppServerEventArgs(
                "token_usage",
                L("AppServerClient.TokenUsage.Updated", "Context usage updated."),
                threadId,
                turnId,
                totalTokens: tokenUsage != null && tokenUsage.Total != null ? (long?)tokenUsage.Total.TotalTokens : null,
                modelContextWindow: tokenUsage != null ? tokenUsage.ModelContextWindow : null,
                threadTokenUsage: tokenUsage);
        }

        internal static AppServerEventArgs CreateConfigWarning(IDictionary<string, object> parameters)
        {
            return CreateProtocolWarning(
                "AppServerClient.Protocol.ConfigWarning.Template",
                "Configuration warning: {0}",
                "AppServerClient.Protocol.ConfigWarning.NoDetails",
                "The runtime reported a configuration warning.",
                parameters);
        }

        internal static AppServerEventArgs CreateDeprecationNotice(IDictionary<string, object> parameters)
        {
            var summary = AppServerJsonValueReader.GetString(parameters, "summary");
            var details = AppServerJsonValueReader.GetString(parameters, "details");
            var message = string.IsNullOrWhiteSpace(summary) ? ExtractRuntimeMessage(parameters) : summary;
            if (!string.IsNullOrWhiteSpace(summary) && !string.IsNullOrWhiteSpace(details) &&
                !string.Equals(summary, details, StringComparison.Ordinal))
                message += Environment.NewLine + details;
            return CreateProtocolWarning(string.IsNullOrWhiteSpace(message)
                ? L("AppServerClient.Protocol.DeprecationNotice.NoDetails", "The runtime reported a deprecation notice.")
                : LF("AppServerClient.Protocol.DeprecationNotice.Template", "Deprecation notice: {0}", message),
                parameters);
        }

        internal static AppServerEventArgs CreateGuardianWarning(IDictionary<string, object> parameters)
        {
            return CreateProtocolWarning(
                "AppServerClient.Protocol.GuardianWarning.Template",
                "Guardian warning: {0}",
                "AppServerClient.Protocol.GuardianWarning.NoDetails",
                "The runtime reported a guardian warning.",
                parameters);
        }

        internal static AppServerEventArgs CreateRuntimeWarning(IDictionary<string, object> parameters)
        {
            return CreateProtocolWarning(
                "AppServerClient.Protocol.RuntimeWarning.Template",
                "Runtime warning: {0}",
                "AppServerClient.Protocol.RuntimeWarning.NoDetails",
                "The runtime reported a warning.",
                parameters);
        }

        internal static AppServerEventArgs CreateWindowsWorldWritableWarning(IDictionary<string, object> parameters)
        {
            var warning = AppServerWorldWritableWarningMapper.BuildEvent(parameters);
            var samples = warning.SamplePaths.Count == 0
                ? L("AppServerClient.Windows.WorldWritableWarning.NoSamples", "(no sample paths)")
                : string.Join("; ", warning.SamplePaths.ToArray());
            var content = warning.FailedScan
                ? L(
                    "AppServerClient.Windows.WorldWritableWarning.ScanFailed",
                    "Windows path permission scan did not complete. Review the diagnostic details.")
                : LF(
                    "AppServerClient.Windows.WorldWritableWarning.Template",
                    "Windows detected broadly writable paths. Samples: {0}.",
                    samples);

            return new AppServerEventArgs(
                "windows_world_writable_warning",
                content,
                details: BuildWorldWritableDetails(warning),
                windowsWorldWritableWarning: warning);
        }

        internal static AppServerEventArgs CreateAutoApprovalReviewStarted(IDictionary<string, object> parameters)
        {
            var review = AppServerGuardianApprovalReviewMapper.BuildStartedEvent(parameters);
            return new AppServerEventArgs(
                "auto_approval_review_started",
                BuildAutoApprovalReviewContent(
                    "AppServerClient.AutoApprovalReview.Started.Template",
                    "Auto approval review started: {0}, risk {1}, action {2}.",
                    review),
                threadId: review.ThreadId,
                turnId: review.TurnId,
                itemId: review.TargetItemId,
                details: BuildAutoApprovalReviewDetails(review),
                mcpToolName: review.Action != null ? review.Action.Title : null,
                itemStatus: review.Status,
                guardianApprovalReview: review);
        }

        internal static AppServerEventArgs CreateAutoApprovalReviewCompleted(IDictionary<string, object> parameters)
        {
            var review = AppServerGuardianApprovalReviewMapper.BuildCompletedEvent(parameters);
            return new AppServerEventArgs(
                "auto_approval_review_completed",
                BuildAutoApprovalReviewContent(
                    "AppServerClient.AutoApprovalReview.Completed.Template",
                    "Auto approval review completed: {0}, status {1}, risk {2}, action {3}.",
                    review),
                requestId: review.ApprovalRequest == null ? null : "guardian:" + review.ThreadId + ":" + review.ReviewId,
                threadId: review.ThreadId,
                turnId: review.TurnId,
                itemId: review.TargetItemId,
                details: BuildAutoApprovalReviewDetails(review),
                mcpToolName: review.Action != null ? review.Action.Title : null,
                itemStatus: review.Status,
                guardianApprovalReview: review);
        }

        internal static AppServerEventArgs CreateTurnModerationMetadata(IDictionary<string, object> parameters)
        {
            var moderation = AppServerModerationMetadataMapper.BuildEvent(parameters);
            return new AppServerEventArgs(
                "turn_moderation_metadata",
                LF(
                    "AppServerClient.ModerationMetadata.Template",
                    "Moderation metadata: {0}",
                    moderation.Summary),
                threadId: moderation.ThreadId,
                turnId: moderation.TurnId,
                details: moderation.Details,
                moderationMetadata: moderation);
        }

        internal static AppServerEventArgs CreateModelRerouted(IDictionary<string, object> parameters)
        {
            var sourceModel = AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "fromModel", "from", "requestedModel", "model");
            var targetModel = AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "toModel", "to", "actualModel", "routedModel");
            var reason = ExtractRuntimeMessage(parameters);
            string content;

            if (!string.IsNullOrWhiteSpace(sourceModel) && !string.IsNullOrWhiteSpace(targetModel))
            {
                content = LF(
                    string.IsNullOrWhiteSpace(reason)
                        ? "AppServerClient.Protocol.ModelRerouted.Template"
                        : "AppServerClient.Protocol.ModelReroutedWithReason.Template",
                    string.IsNullOrWhiteSpace(reason)
                        ? "Model rerouted: {0} -> {1}"
                        : "Model rerouted: {0} -> {1}. {2}",
                    sourceModel,
                    targetModel,
                    reason);
            }
            else
            {
                content = string.IsNullOrWhiteSpace(reason)
                    ? L("AppServerClient.Protocol.ModelRerouted.NoDetails", "The runtime rerouted the model.")
                    : LF("AppServerClient.Protocol.ModelRerouted.Message.Template", "Model rerouted: {0}", reason);
            }

            return CreateProtocolNotice(content, parameters);
        }

        internal static AppServerEventArgs CreateModelVerification(IDictionary<string, object> parameters)
        {
            var message = ExtractRuntimeMessage(parameters);
            var status = AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "status", "state", "result");
            var model = AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "model", "modelId", "provider");
            var detail = !string.IsNullOrWhiteSpace(message)
                ? message
                : (!string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(status)
                    ? LF("AppServerClient.Protocol.ModelVerification.ModelStatus.Template", "{0}: {1}", model, status)
                    : (!string.IsNullOrWhiteSpace(status) ? status : null));
            var content = string.IsNullOrWhiteSpace(detail)
                ? L("AppServerClient.Protocol.ModelVerification.NoDetails", "The runtime reported a model verification update.")
                : LF("AppServerClient.Protocol.ModelVerification.Template", "Model verification: {0}", detail);

            return IsWarningStatus(status, message)
                ? CreateProtocolWarning(content, parameters)
                : CreateProtocolNotice(content, parameters);
        }

        internal static AppServerEventArgs CreateUnhandledNotification(string method, IDictionary<string, object> parameters)
        {
            var normalizedMethod = NormalizeMethod(method);
            var message = ExtractRuntimeMessage(parameters);
            var content = string.IsNullOrWhiteSpace(message)
                ? LF(
                    "AppServerClient.Protocol.UnhandledNotification.Template",
                    "Received an unhandled App Server notification: {0}",
                    normalizedMethod)
                : LF(
                    "AppServerClient.Protocol.UnhandledNotificationWithMessage.Template",
                    "Received an unhandled App Server notification: {0}. {1}",
                    normalizedMethod,
                    message);
            return CreateProtocolWarning(content, parameters, normalizedMethod);
        }

        internal static AppServerEventArgs CreateUnhandledServerRequest(string method, IDictionary<string, object> parameters)
        {
            var normalizedMethod = NormalizeMethod(method);
            var message = ExtractRuntimeMessage(parameters);
            var content = string.IsNullOrWhiteSpace(message)
                ? LF(
                    "AppServerClient.Protocol.UnhandledServerRequest.Template",
                    "Received an unhandled App Server request: {0}",
                    normalizedMethod)
                : LF(
                    "AppServerClient.Protocol.UnhandledServerRequestWithMessage.Template",
                    "Received an unhandled App Server request: {0}. {1}",
                    normalizedMethod,
                    message);
            return CreateProtocolWarning(content, parameters, normalizedMethod);
        }

        private static AppServerEventArgs CreateProtocolWarning(
            string templateKey,
            string fallbackTemplate,
            string emptyKey,
            string emptyFallback,
            IDictionary<string, object> parameters)
        {
            var message = ExtractRuntimeMessage(parameters);
            var content = string.IsNullOrWhiteSpace(message)
                ? L(emptyKey, emptyFallback)
                : LF(templateKey, fallbackTemplate, message);
            return CreateProtocolWarning(content, parameters);
        }

        private static AppServerEventArgs CreateProtocolWarning(
            string content,
            IDictionary<string, object> parameters,
            string details = null)
        {
            return new AppServerEventArgs(
                "protocol_warning",
                content,
                threadId: AppServerJsonValueReader.GetString(parameters, "threadId"),
                turnId: AppServerJsonValueReader.GetString(parameters, "turnId"),
                details: details);
        }

        private static AppServerEventArgs CreateProtocolNotice(
            string content,
            IDictionary<string, object> parameters,
            string details = null)
        {
            return new AppServerEventArgs(
                "protocol_notice",
                content,
                threadId: AppServerJsonValueReader.GetString(parameters, "threadId"),
                turnId: AppServerJsonValueReader.GetString(parameters, "turnId"),
                details: details);
        }

        private static string BuildWorldWritableDetails(AppServerWindowsWorldWritableWarningEvent warning)
        {
            var lines = new List<string>();
            lines.Add("windows/worldWritableWarning");
            lines.Add("failedScan=" + warning.FailedScan);
            lines.Add("extraCount=" + warning.ExtraCount.ToString(CultureInfo.InvariantCulture));
            for (var i = 0; i < warning.SamplePaths.Count; i++)
            {
                lines.Add(warning.SamplePaths[i]);
            }

            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private static string BuildAutoApprovalReviewContent(string key, string fallback, AppServerGuardianApprovalReviewEvent review)
        {
            var reviewId = string.IsNullOrWhiteSpace(review.ReviewId)
                ? L("AppServerClient.AutoApprovalReview.UnknownReview", "(unknown review)")
                : review.ReviewId;
            var risk = string.IsNullOrWhiteSpace(review.RiskLevel)
                ? L("AppServerClient.AutoApprovalReview.UnknownRisk", "unknown")
                : review.RiskLevel;
            var status = string.IsNullOrWhiteSpace(review.Status)
                ? L("AppServerClient.AutoApprovalReview.UnknownStatus", "unknown")
                : review.Status;
            var action = review.Action == null || string.IsNullOrWhiteSpace(review.Action.Title)
                ? L("AppServerClient.AutoApprovalReview.UnknownAction", "Guardian action")
                : review.Action.Title;

            return review.IsCompleted
                ? LF(key, fallback, reviewId, status, risk, action)
                : LF(key, fallback, reviewId, risk, action);
        }

        private static string BuildAutoApprovalReviewDetails(AppServerGuardianApprovalReviewEvent review)
        {
            var lines = new List<string>();
            lines.Add(review.IsCompleted ? "item/autoApprovalReview/completed" : "item/autoApprovalReview/started");
            AddDetail(lines, "reviewId", review.ReviewId);
            AddDetail(lines, "status", review.Status);
            AddDetail(lines, "riskLevel", review.RiskLevel);
            AddDetail(lines, "decisionSource", review.DecisionSource);
            AddDetail(lines, "targetItemId", review.TargetItemId);
            if (review.Action != null)
            {
                AddDetail(lines, "actionType", review.Action.ActionType);
                AddDetail(lines, "action", review.Action.Title);
                AddDetail(lines, "actionDetails", review.Action.Details);
            }

            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private static void AddDetail(List<string> lines, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                lines.Add(name + "=" + value);
            }
        }

        private static string ExtractRuntimeMessage(IDictionary<string, object> parameters)
        {
            var message = AppServerJsonValueReader.GetFirstNonEmptyString(
                parameters,
                "message",
                "text",
                "description",
                "reason",
                "details",
                "warning",
                "notice",
                "error");
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }

            var error = AppServerJsonValueReader.GetDictionary(parameters, "error");
            message = AppServerJsonValueReader.GetFirstNonEmptyString(error, "message", "text", "description", "reason");
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }

            var warning = AppServerJsonValueReader.GetDictionary(parameters, "warning");
            message = AppServerJsonValueReader.GetFirstNonEmptyString(warning, "message", "text", "description", "reason");
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }

            var notice = AppServerJsonValueReader.GetDictionary(parameters, "notice");
            message = AppServerJsonValueReader.GetFirstNonEmptyString(notice, "message", "text", "description", "reason");
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }

            var status = AppServerJsonValueReader.GetDictionary(parameters, "status");
            return AppServerJsonValueReader.GetFirstNonEmptyString(status, "message", "text", "description", "reason", "error");
        }

        private static string ExtractStatusText(IDictionary<string, object> parameters)
        {
            var status = AppServerJsonValueReader.GetDictionary(parameters, "status");
            var statusText = AppServerJsonValueReader.GetFirstNonEmptyString(status, "type", "status", "state", "phase");
            if (!string.IsNullOrWhiteSpace(statusText))
            {
                return statusText;
            }

            return AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "status", "state", "phase", "startupStatus");
        }

        private static string ExtractMcpStartupFailureReason(IDictionary<string, object> parameters)
        {
            var reason = AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "failureReason");
            return string.IsNullOrWhiteSpace(reason)
                ? AppServerJsonValueReader.GetFirstNonEmptyString(AppServerJsonValueReader.GetDictionary(parameters, "status"), "failureReason", "reason")
                : reason;
        }

        private static string GetSafeMcpReauthenticationMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message) ||
                message.IndexOf("http://", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("https://", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return null;
            }

            return GitOutputSanitizer.RedactSensitiveText(message);
        }

        private static string NormalizeMethod(string method)
        {
            return string.IsNullOrWhiteSpace(method)
                ? L("AppServerClient.Protocol.UnknownMethod", "(unknown method)")
                : method.Trim();
        }

        private static bool IsWarningStatus(string status, string message)
        {
            return ContainsWarningWord(status) || ContainsWarningWord(message);
        }

        private static bool ContainsWarningWord(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return text.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("warn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("blocked", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key, fallback), args ?? Array.Empty<object>());
        }
    }
}
