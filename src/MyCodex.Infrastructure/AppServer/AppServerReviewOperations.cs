using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerReviewOperations
    {
        private readonly AppServerRequestInvoker _send;
        private readonly bool _excludeTurns;

        internal AppServerReviewOperations(AppServerRequestInvoker send, bool excludeTurns = false)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _excludeTurns = excludeTurns;
        }

        internal async Task<AppServerReviewStartResult> StartAsync(
            string threadId, string serverWorkspacePath, ApprovalMode? approvalMode,
            string modelId, string reasoningEffort, string reviewModelId,
            AppServerReviewTarget target, bool detached, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(threadId)) throw new ArgumentException(nameof(threadId));
            if (string.IsNullOrWhiteSpace(serverWorkspacePath)) throw new ArgumentException(nameof(serverWorkspacePath));
            if (target == null) throw new ArgumentNullException(nameof(target));
            target.Validate();
            if (string.IsNullOrWhiteSpace(modelId))
            {
                throw SettingsUnavailable();
            }

            if (!detached)
            {
                var identity = new Dictionary<string, object> { { "threadId", threadId } };
                var read = await SendAsync("thread/read", identity, cancellationToken).ConfigureAwait(false);
                EnsureIdle(GetDictionary(read, "thread"));
                await SendAsync("thread/unsubscribe", identity, cancellationToken).ConfigureAwait(false);
            }

            var parameters = AppServerThreadStartParameterBuilder.Build(
                serverWorkspacePath, approvalMode, modelId, null);
            parameters.Remove("experimentalRawEvents");
            parameters.Remove("persistExtendedHistory");
            if (!detached) parameters["threadId"] = threadId;
            if (!detached && _excludeTurns) parameters["excludeTurns"] = true;
            var config = GetDictionary(parameters, "config");
            parameters["config"] = config;
            if (!string.IsNullOrWhiteSpace(reasoningEffort)) config["model_reasoning_effort"] = reasoningEffort;
            var effectiveReviewModel = string.IsNullOrWhiteSpace(reviewModelId) ? modelId : reviewModelId;
            config["review_model"] = effectiveReviewModel;

            var resumed = await SendAsync(detached ? "thread/start" : "thread/resume", parameters, cancellationToken).ConfigureAwait(false);
            var preparedId = GetString(GetDictionary(resumed, "thread"), "id");
            var result = new AppServerReviewStartResult
            {
                ReviewThreadId = preparedId,
                ConfiguredModelId = effectiveReviewModel,
                ConfiguredReasoningEffort = GetString(resumed, "reasoningEffort"),
                WorkingDirectory = serverWorkspacePath,
                CreatedThread = detached && !string.IsNullOrWhiteSpace(preparedId) &&
                    !string.Equals(preparedId, threadId, StringComparison.Ordinal)
                    ? new AppServerThreadSummary { ThreadId = preparedId, WorkingDirectory = serverWorkspacePath }
                    : null
            };
            try
            {
                EnsureIdle(GetDictionary(resumed, "thread"));
                if (string.IsNullOrWhiteSpace(preparedId) ||
                    (!detached && !string.Equals(preparedId, threadId, StringComparison.Ordinal)) ||
                    (detached && string.Equals(preparedId, threadId, StringComparison.Ordinal)) ||
                    !SamePath(serverWorkspacePath, GetString(resumed, "cwd")) ||
                    !string.Equals(modelId, GetString(resumed, "model"), StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(reasoningEffort) &&
                     !string.Equals(reasoningEffort, GetString(resumed, "reasoningEffort"), StringComparison.Ordinal)) ||
                    (approvalMode.HasValue && !PermissionsMatch(parameters, resumed)))
                {
                    throw SettingsUnavailable();
                }
                var response = await SendAsync("review/start", new Dictionary<string, object>
                {
                    { "threadId", preparedId },
                    { "target", BuildTarget(target) },
                    { "delivery", "inline" }
                }, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(GetString(response, "reviewThreadId"), preparedId, StringComparison.Ordinal))
                    throw SettingsUnavailable();
                result.TurnId = GetString(GetDictionary(response, "turn"), "id");
                return result;
            }
            catch (Exception ex) when (result.CreatedThread != null)
            {
                // Keep the created identity available even when preparation, cancellation,
                // or review/start fails; an empty new thread may not be readable yet.
                throw new AppServerReviewStartException(result, ex);
            }
        }

        private static void EnsureIdle(IDictionary<string, object> thread)
        {
            var status = GetString(GetDictionary(thread, "status"), "type");
            if (!string.Equals(status, "idle", StringComparison.Ordinal) &&
                !string.Equals(status, "notLoaded", StringComparison.Ordinal))
            {
                throw SettingsUnavailable();
            }
        }

        private static bool PermissionsMatch(IDictionary<string, object> requested, IDictionary<string, object> actual)
        {
            var sandbox = GetString(GetDictionary(actual, "sandbox"), "type");
            var expected = GetString(requested, "sandbox");
            var actualMode = sandbox == "readOnly" ? "read-only" :
                sandbox == "workspaceWrite" ? "workspace-write" :
                sandbox == "dangerFullAccess" ? "danger-full-access" : null;
            return string.Equals(expected, actualMode, StringComparison.Ordinal) &&
                string.Equals(GetString(requested, "approvalsReviewer"), GetString(actual, "approvalsReviewer"), StringComparison.Ordinal) &&
                string.Equals(GetString(requested, "approvalPolicy"), GetString(actual, "approvalPolicy"), StringComparison.Ordinal);
        }

        private static bool SamePath(string requested, string actual)
        {
            var unix = AppServerPathUtilities.LooksLikeUnixAbsolutePath(requested);
            return string.Equals(requested.TrimEnd('/', '\\'), (actual ?? string.Empty).TrimEnd('/', '\\'),
                unix ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        private static InvalidOperationException SettingsUnavailable()
        {
            return new InvalidOperationException(SharedLocalization.Text("AppServerReview.SettingsUnavailable",
                "The review directory or settings could not be applied. The thread may still be active or used by another client. Review was not started."));
        }

        private Task<IDictionary<string, object>> SendAsync(string method, object parameters, CancellationToken cancellationToken)
        {
            return _send(method, parameters, cancellationToken, 60000);
        }

        internal static IDictionary<string, object> BuildTarget(AppServerReviewTarget target)
        {
            target.Validate();
            switch (target.Kind)
            {
                case AppServerReviewTargetKind.UncommittedChanges:
                    return new Dictionary<string, object> { { "type", "uncommittedChanges" } };
                case AppServerReviewTargetKind.BaseBranch:
                    return new Dictionary<string, object> { { "type", "baseBranch" }, { "branch", target.Branch.Trim() } };
                case AppServerReviewTargetKind.Commit:
                    var result = new Dictionary<string, object> { { "type", "commit" }, { "sha", target.Sha.Trim() } };
                    if (!string.IsNullOrWhiteSpace(target.Title)) result["title"] = target.Title.Trim();
                    return result;
                case AppServerReviewTargetKind.Custom:
                    return new Dictionary<string, object> { { "type", "custom" }, { "instructions", target.Instructions.Trim() } };
                default:
                    throw new InvalidOperationException(SharedLocalization.Text("AppServerClient.Error.UnsupportedReviewTarget", "Unsupported review target."));
            }
        }

        private static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetDictionary(source, key);
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetString(source, key);
        }
    }
}
