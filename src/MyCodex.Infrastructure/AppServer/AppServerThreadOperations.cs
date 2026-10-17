using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerThreadOperations
    {
        private const int DefaultRequestTimeoutMs = 60000;
        private const int LongRunningRequestTimeoutMs = 300000;
        private const int ConversationSummaryEnrichmentLimit = 48;

        private readonly Action _ensureConnected;
        private readonly Func<AppServerProtocolCapabilities> _getProtocolCapabilities;
        private readonly AppServerRequestInvoker _sendRequestAsync;
        private readonly Func<string, string, string> _mapRequiredClientPathToServer;
        private readonly Func<string, string> _mapServerPathToClient;
        private readonly Func<string, string, ApprovalMode?, Task> _ensureThreadLoadedAsync;
        private readonly Func<string, Task> _ensureWslNetworkReadyForConnectedActionAsync;
        private readonly Func<Task<IReadOnlyList<string>>> _resolveLocalCodexHomePathsForThreadScanAsync;
        private readonly Func<int, CancellationToken, Task<List<AppServerThreadSummary>>> _listRuntimeThreadSummariesForThreadScanAsync;
        private readonly Func<AppServerConversationSummaryRequest, CancellationToken, Task<AppServerConversationSummary>> _getConversationSummaryAsync;
        private readonly Func<IEnumerable<string>> _getLocallyKnownLoadedThreadIds;
        private readonly Action<string> _rememberLoadedThreadId;
        private readonly Action<string> _forgetThreadState;

        public AppServerThreadOperations(
            Action ensureConnected,
            Func<AppServerProtocolCapabilities> getProtocolCapabilities,
            AppServerRequestInvoker sendRequestAsync,
            Func<string, string, string> mapRequiredClientPathToServer,
            Func<string, string> mapServerPathToClient,
            Func<string, string, ApprovalMode?, Task> ensureThreadLoadedAsync,
            Func<string, Task> ensureWslNetworkReadyForConnectedActionAsync,
            Func<Task<IReadOnlyList<string>>> resolveLocalCodexHomePathsForThreadScanAsync,
            Func<int, CancellationToken, Task<List<AppServerThreadSummary>>> listRuntimeThreadSummariesForThreadScanAsync,
            Func<AppServerConversationSummaryRequest, CancellationToken, Task<AppServerConversationSummary>> getConversationSummaryAsync,
            Func<IEnumerable<string>> getLocallyKnownLoadedThreadIds,
            Action<string> rememberLoadedThreadId,
            Action<string> forgetThreadState)
        {
            _ensureConnected = ensureConnected ?? throw new ArgumentNullException(nameof(ensureConnected));
            _getProtocolCapabilities = getProtocolCapabilities ?? throw new ArgumentNullException(nameof(getProtocolCapabilities));
            _sendRequestAsync = sendRequestAsync ?? throw new ArgumentNullException(nameof(sendRequestAsync));
            _mapRequiredClientPathToServer = mapRequiredClientPathToServer ?? throw new ArgumentNullException(nameof(mapRequiredClientPathToServer));
            _mapServerPathToClient = mapServerPathToClient ?? throw new ArgumentNullException(nameof(mapServerPathToClient));
            _ensureThreadLoadedAsync = ensureThreadLoadedAsync ?? throw new ArgumentNullException(nameof(ensureThreadLoadedAsync));
            _ensureWslNetworkReadyForConnectedActionAsync = ensureWslNetworkReadyForConnectedActionAsync ?? throw new ArgumentNullException(nameof(ensureWslNetworkReadyForConnectedActionAsync));
            _resolveLocalCodexHomePathsForThreadScanAsync = resolveLocalCodexHomePathsForThreadScanAsync ?? throw new ArgumentNullException(nameof(resolveLocalCodexHomePathsForThreadScanAsync));
            _listRuntimeThreadSummariesForThreadScanAsync = listRuntimeThreadSummariesForThreadScanAsync ?? throw new ArgumentNullException(nameof(listRuntimeThreadSummariesForThreadScanAsync));
            _getConversationSummaryAsync = getConversationSummaryAsync ?? throw new ArgumentNullException(nameof(getConversationSummaryAsync));
            _getLocallyKnownLoadedThreadIds = getLocallyKnownLoadedThreadIds ?? throw new ArgumentNullException(nameof(getLocallyKnownLoadedThreadIds));
            _rememberLoadedThreadId = rememberLoadedThreadId ?? throw new ArgumentNullException(nameof(rememberLoadedThreadId));
            _forgetThreadState = forgetThreadState ?? throw new ArgumentNullException(nameof(forgetThreadState));
        }

        public Task<List<AppServerThreadSummary>> ListThreadsAsync(CancellationToken cancellationToken)
        {
            return ListThreadsCoreAsync(false, null, cancellationToken);
        }

        public async Task<List<string>> ListLoadedThreadIdsAsync(int? limit, CancellationToken cancellationToken)
        {
            _ensureConnected();

            var loadedThreadIds = new List<string>();
            string cursor = null;

            do
            {
                var parameters = new Dictionary<string, object>();
                if (limit.HasValue && limit.Value > 0)
                {
                    parameters["limit"] = limit.Value;
                }

                if (!string.IsNullOrWhiteSpace(cursor))
                {
                    parameters["cursor"] = cursor;
                }

                var result = await SendRequestAsync("thread/loaded/list", parameters, cancellationToken).ConfigureAwait(false);
                foreach (var item in GetArray(result, "data"))
                {
                    var threadId = item as string;
                    if (string.IsNullOrWhiteSpace(threadId))
                    {
                        var itemObject = item as IDictionary<string, object>;
                        threadId = GetString(itemObject, "threadId") ?? GetString(itemObject, "id");
                    }

                    if (!string.IsNullOrWhiteSpace(threadId))
                    {
                        loadedThreadIds.Add(threadId);
                    }
                }

                cursor = GetString(result, "nextCursor");
            }
            while (!string.IsNullOrWhiteSpace(cursor) && (!limit.HasValue || loadedThreadIds.Count < limit.Value));

            return MergeLoadedThreadIdsForSync(loadedThreadIds, _getLocallyKnownLoadedThreadIds(), limit);
        }

        public Task<List<AppServerThreadSummary>> ListArchivedThreadsAsync(string workspacePath)
        {
            return ListThreadsCoreAsync(true, workspacePath, CancellationToken.None);
        }

        public async Task<AppServerConversationSummary> GetConversationSummaryAsync(
            AppServerConversationSummaryRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _ensureConnected();
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            request.Validate();

            var parameters = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(request.ConversationId))
            {
                parameters["conversationId"] = request.ConversationId.Trim();
            }
            else
            {
                parameters["rolloutPath"] = _mapRequiredClientPathToServer(request.RolloutPath.Trim(), "rollout");
            }

            var result = await SendRequestAsync("getConversationSummary", parameters, cancellationToken).ConfigureAwait(false);
            return AppServerConversationSummaryMapper.BuildSummary(GetDictionary(result, "summary"), _mapServerPathToClient);
        }

        public async Task<AppServerThreadDetails> ReadThreadAsync(string threadId, CancellationToken cancellationToken)
        {
            _ensureConnected();
            var thread = await AppServerThreadHistoryReader.ReadAsync(
                _sendRequestAsync, threadId,
                _getProtocolCapabilities()?.SupportsThreadHistoryPagination == true,
                cancellationToken).ConfigureAwait(false);
            var watch = Stopwatch.StartNew();
            var details = AppServerThreadMapper.BuildThreadDetails(thread, _mapServerPathToClient);
            AppServerHistoryLoadTrace.Information("map", threadId, "Turns={0}; Messages={1}; Activities={2}; ElapsedMs={3}",
                details.TurnCount, details.Messages.Count, details.Activities.Count, watch.ElapsedMilliseconds);
            return details;
        }

        internal async Task<bool> ConfirmThreadMissingAsync(string threadId, string sessionPath,
            Func<string, Task<AppServerFileSystemMetadata>> getMetadata)
        {
            _ensureConnected();
            EnsureThreadId(threadId);
            if (string.IsNullOrWhiteSpace(sessionPath)) return false;
            foreach (var archived in new[] { false, true })
            {
                string cursor = null;
                var cursors = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    var parameters = BuildThreadListParameters(archived, null, cursor);
                    var result = await SendRequestAsync("thread/list", parameters).ConfigureAwait(false);
                    if (result == null || !result.ContainsKey("data") || result["data"] == null) return false;
                    foreach (var entry in GetArray(result, "data"))
                    {
                        var thread = entry as IDictionary<string, object>;
                        if (thread == null || string.IsNullOrWhiteSpace(GetString(thread, "id"))) return false;
                        if (string.Equals(GetString(thread, "id"), threadId, StringComparison.Ordinal)) return false;
                    }
                    cursor = GetString(result, "nextCursor");
                    if (!string.IsNullOrWhiteSpace(cursor) && !cursors.Add(cursor)) return false;
                }
                while (!string.IsNullOrWhiteSpace(cursor));
            }
            if ((await ListLoadedThreadIdsAsync(null, CancellationToken.None).ConfigureAwait(false)).Contains(threadId)) return false;
            try
            {
                await getMetadata(sessionPath).ConfigureAwait(false);
                return false;
            }
            catch (InvalidOperationException ex) when (ex.Message.IndexOf("(os error 2)", StringComparison.Ordinal) >= 0 ||
                ex.Message.IndexOf("(os error 3)", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        public async Task ArchiveThreadAsync(string threadId)
        {
            _ensureConnected();
            EnsureThreadId(threadId);

            await SendRequestAsync("thread/archive", new Dictionary<string, object>
            {
                { "threadId", threadId }
            }).ConfigureAwait(false);
        }

        public async Task DeleteThreadAsync(string threadId)
        {
            _ensureConnected();
            EnsureThreadId(threadId);

            try
            {
                await SendRequestAsync("thread/delete", new Dictionary<string, object>
                {
                    { "threadId", threadId }
                }).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.StartsWith("cannot delete thread ", StringComparison.Ordinal) &&
                ex.Message.EndsWith(": forked history still references it", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(L(
                    "AppServerClient.Error.ThreadReferencedByFork",
                    "Cannot permanently delete this history because a forked conversation still references it. You can archive the conversation instead."), ex);
            }

            _forgetThreadState(threadId);
        }

        public async Task UnarchiveThreadAsync(string threadId)
        {
            _ensureConnected();
            EnsureThreadId(threadId);

            await SendRequestAsync("thread/unarchive", new Dictionary<string, object>
            {
                { "threadId", threadId }
            }).ConfigureAwait(false);
        }

        public Task<AppServerThreadSummary> ForkThreadAsync(string threadId)
        {
            return ForkThreadAsync(threadId, null);
        }

        public Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId)
        {
            return ForkThreadAsync(threadId, lastTurnId, null, null, null, null);
        }

        public async Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId, string workspacePath, ApprovalMode? approvalMode, string modelId, string reasoningEffort)
        {
            _ensureConnected();
            EnsureThreadId(threadId);

            if (!string.IsNullOrWhiteSpace(lastTurnId))
            {
                EnsureSupported(capabilities => capabilities.SupportsThreadForkLastTurnId, "Turn-bounded thread fork");
            }

            var parameters = new Dictionary<string, object>
            {
                { "threadId", threadId }
            };
            AddOptionalString(parameters, "lastTurnId", lastTurnId);
            if (_getProtocolCapabilities()?.SupportsThreadHistoryPagination == true)
                parameters["excludeTurns"] = true;
            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                var overrides = AppServerThreadStartParameterBuilder.Build(
                    _mapRequiredClientPathToServer(workspacePath, "workspace"), approvalMode, modelId, null);
                overrides.Remove("experimentalRawEvents");
                overrides.Remove("persistExtendedHistory");
                foreach (var entry in overrides)
                {
                    parameters[entry.Key] = entry.Value;
                }
            }
            AddOptionalString(parameters, "model", modelId);
            if (!string.IsNullOrWhiteSpace(reasoningEffort))
            {
                object configValue;
                var config = parameters.TryGetValue("config", out configValue)
                    ? (IDictionary<string, object>)configValue : new Dictionary<string, object>();
                config["model_reasoning_effort"] = reasoningEffort;
                parameters["config"] = config;
            }

            var result = await SendRequestAsync("thread/fork", parameters).ConfigureAwait(false);

            var thread = GetDictionary(result, "thread");
            var summary = AppServerThreadMapper.BuildThreadSummary(thread, _mapServerPathToClient);
            if (summary == null || string.IsNullOrWhiteSpace(summary.ThreadId))
            {
                throw new InvalidOperationException(L("AppServerClient.Error.ForkedThreadIdMissing", "App Server did not return the forked thread ID."));
            }

            _rememberLoadedThreadId(summary.ThreadId);
            return summary;
        }

        public async Task CompactThreadAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
        {
            _ensureConnected();
            EnsureThreadId(threadId);

            await _ensureThreadLoadedAsync(threadId, workspacePath, approvalMode).ConfigureAwait(false);
            await _ensureWslNetworkReadyForConnectedActionAsync("thread/compact/start").ConfigureAwait(false);

            await SendRequestAsync("thread/compact/start", new Dictionary<string, object>
            {
                { "threadId", threadId }
            }, CancellationToken.None, LongRunningRequestTimeoutMs).ConfigureAwait(false);
        }

        public async Task<AppServerThreadInjectItemsResult> InjectThreadItemsAsync(
            string threadId,
            IReadOnlyCollection<IDictionary<string, object>> items,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _ensureConnected();
            EnsureThreadId(threadId);

            if (items == null || items.Count == 0)
            {
                throw new ArgumentException(L("AppServerClient.Error.ValidInputRequired", "At least one valid input item is required."), nameof(items));
            }

            await SendRequestAsync(
                "thread/inject_items",
                new Dictionary<string, object>
                {
                    { "threadId", threadId.Trim() },
                    { "items", items.ToArray() }
                },
                cancellationToken)
                .ConfigureAwait(false);

            return new AppServerThreadInjectItemsResult
            {
                Succeeded = true
            };
        }

        public Task ApproveGuardianDeniedActionAsync(
            AppServerGuardianDeniedActionApprovalRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _ensureConnected();
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.ThreadId))
            {
                throw new ArgumentException(L("AppServerClient.Error.ThreadIdRequired", "Thread ID cannot be empty."), nameof(request));
            }

            if (request.Event == null)
            {
                throw new ArgumentException(L("AppServerClient.Error.GuardianEventRequired", "Guardian assessment event cannot be empty."), nameof(request));
            }

            return SendRequestAsync(
                "thread/approveGuardianDeniedAction",
                new Dictionary<string, object>
                {
                    { "threadId", request.ThreadId.Trim() },
                    { "event", request.Event }
                },
                cancellationToken);
        }

        public async Task UnsubscribeThreadAsync(string threadId)
        {
            _ensureConnected();

            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            await SendRequestAsync("thread/unsubscribe", new Dictionary<string, object>
            {
                { "threadId", threadId }
            }).ConfigureAwait(false);

            _forgetThreadState(threadId);
        }

        public async Task SetThreadNameAsync(string threadId, string name)
        {
            _ensureConnected();
            EnsureThreadId(threadId);

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(L("AppServerClient.Error.ThreadNameRequired", "Thread name cannot be empty."), nameof(name));
            }

            await SendRequestAsync("thread/name/set", new Dictionary<string, object>
            {
                { "threadId", threadId },
                { "name", name }
            }).ConfigureAwait(false);
        }

        public async Task<AppServerThreadGoal> SetThreadGoalAsync(
            string threadId,
            string workspacePath,
            ApprovalMode? approvalMode,
            string modelId,
            string objective,
            long? tokenBudget = null)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsExperimentalApi, "Thread goals");
            EnsureThreadId(threadId);

            if (string.IsNullOrWhiteSpace(objective))
            {
                throw new ArgumentException(L("AppServerClient.Error.ThreadGoalObjectiveRequired", "Thread goal objective cannot be empty."), nameof(objective));
            }

            await _ensureThreadLoadedAsync(threadId, workspacePath, approvalMode).ConfigureAwait(false);

            var parameters = new Dictionary<string, object>
            {
                { "threadId", threadId },
                { "objective", objective.Trim() }
            };
            if (tokenBudget.HasValue)
            {
                parameters["tokenBudget"] = tokenBudget.Value;
            }

            var result = await SendRequestAsync("thread/goal/set", parameters).ConfigureAwait(false);
            return AppServerProtocolMetadataMapper.BuildThreadGoalFromResult(result, threadId);
        }

        public async Task<AppServerThreadGoal> GetThreadGoalAsync(
            string threadId,
            string workspacePath,
            ApprovalMode? approvalMode,
            string modelId)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsExperimentalApi, "Thread goals");
            EnsureThreadId(threadId);

            await _ensureThreadLoadedAsync(threadId, workspacePath, approvalMode).ConfigureAwait(false);

            var result = await SendRequestAsync("thread/goal/get", new Dictionary<string, object>
            {
                { "threadId", threadId }
            }).ConfigureAwait(false);
            return AppServerProtocolMetadataMapper.BuildThreadGoalFromResult(result, threadId);
        }

        public async Task ClearThreadGoalAsync(
            string threadId,
            string workspacePath,
            ApprovalMode? approvalMode,
            string modelId)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsExperimentalApi, "Thread goals");
            EnsureThreadId(threadId);

            await _ensureThreadLoadedAsync(threadId, workspacePath, approvalMode).ConfigureAwait(false);

            await SendRequestAsync("thread/goal/clear", new Dictionary<string, object>
            {
                { "threadId", threadId }
            }).ConfigureAwait(false);
        }

        public async Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo)
        {
            _ensureConnected();
            EnsureThreadId(threadId);

            if (gitInfo == null || !gitInfo.HasAnyUpdates)
            {
                return;
            }

            var gitInfoParameters = new Dictionary<string, object>();
            AddOptionalMetadataPatch(gitInfoParameters, "originUrl", gitInfo.HasOriginUrl, gitInfo.OriginUrl);
            AddOptionalMetadataPatch(gitInfoParameters, "branch", gitInfo.HasBranch, gitInfo.Branch);
            AddOptionalMetadataPatch(gitInfoParameters, "sha", gitInfo.HasSha, gitInfo.Sha);
            if (gitInfoParameters.Count == 0)
            {
                return;
            }

            await SendRequestAsync("thread/metadata/update", new Dictionary<string, object>
            {
                { "threadId", threadId },
                { "gitInfo", gitInfoParameters }
            }).ConfigureAwait(false);
        }

        internal static List<string> MergeLoadedThreadIdsForSync(
            IEnumerable<string> remoteThreadIds,
            IEnumerable<string> locallyKnownThreadIds,
            int? limit)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddLoadedThreadIds(remoteThreadIds, result, seen, limit);
            AddLoadedThreadIds(locallyKnownThreadIds, result, seen, limit);
            return result;
        }

        internal static Dictionary<string, object> BuildThreadListParameters(
            bool archived,
            string mappedWorkspacePath,
            string cursor)
        {
            var parameters = new Dictionary<string, object>
            {
                { "limit", 100 },
                { "sortKey", "updated_at" },
                { "archived", archived },
                // Omitted providers default to the current provider; an empty list includes all.
                { "modelProviders", Array.Empty<string>() },
                { "sourceKinds", new[] { "cli", "vscode", "exec", "appServer", "subAgent",
                    "subAgentReview", "subAgentCompact", "subAgentThreadSpawn", "subAgentOther", "unknown" } }
            };

            AddOptionalString(parameters, "cwd", mappedWorkspacePath);

            if (!string.IsNullOrWhiteSpace(cursor))
            {
                parameters["cursor"] = cursor;
            }

            return parameters;
        }

        private async Task<List<AppServerThreadSummary>> ListThreadsCoreAsync(bool archived, string workspacePath, CancellationToken cancellationToken)
        {
            _ensureConnected();

            var threads = new List<AppServerThreadSummary>();
            var mergedThreadMap = new Dictionary<string, AppServerThreadSummary>(StringComparer.OrdinalIgnoreCase);
            var appServerThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string cursor = null;

            do
            {
                var parameters = BuildThreadListParameters(
                    archived,
                    _mapRequiredClientPathToServer(workspacePath, "workspace"),
                    cursor);

                var result = await SendRequestAsync("thread/list", parameters, cancellationToken).ConfigureAwait(false);
                foreach (var item in GetArray(result, "data"))
                {
                    var thread = item as IDictionary<string, object>;
                    if (thread == null)
                    {
                        continue;
                    }

                    var summary = AppServerThreadMapper.BuildThreadSummary(thread, _mapServerPathToClient);
                    if (summary != null && !string.IsNullOrWhiteSpace(summary.ThreadId))
                    {
                        appServerThreadIds.Add(summary.ThreadId);
                    }

                    LocalSessionCatalog.MergeThreadSummary(threads, mergedThreadMap, summary);
                }

                cursor = GetString(result, "nextCursor");
            }
            while (!string.IsNullOrWhiteSpace(cursor));

            try
            {
                foreach (var localCodexHomePath in await _resolveLocalCodexHomePathsForThreadScanAsync().ConfigureAwait(false))
                {
                    try
                    {
                        foreach (var localThread in LocalSessionCatalog.EnumerateLocalThreadSummaries(
                            archived,
                            workspacePath,
                            localCodexHomePath,
                            512,
                            cancellationToken))
                        {
                            LocalSessionCatalog.MergeThreadSummary(threads, mergedThreadMap, localThread);
                        }
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        Trace.TraceWarning("Failed to merge local thread summaries from {0}: {1}", localCodexHomePath, ex.Message);
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Trace.TraceWarning("Failed to merge local thread summaries: {0}", ex.Message);
            }

            if (!archived)
            {
                try
                {
                    foreach (var runtimeThread in await _listRuntimeThreadSummariesForThreadScanAsync(512, cancellationToken).ConfigureAwait(false))
                    {
                        LocalSessionCatalog.MergeThreadSummary(threads, mergedThreadMap, runtimeThread);
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Trace.TraceWarning("Failed to merge runtime thread summaries: {0}", ex.Message);
                }
            }

            await TryEnrichLocalThreadSummariesWithConversationSummaryAsync(
                threads,
                appServerThreadIds,
                cancellationToken).ConfigureAwait(false);

            threads.Sort((left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
            return threads;
        }

        public async Task TryEnrichLocalThreadSummariesWithConversationSummaryAsync(
            List<AppServerThreadSummary> summaries,
            ISet<string> appServerThreadIds,
            CancellationToken cancellationToken)
        {
            if (summaries == null || summaries.Count == 0)
            {
                return;
            }

            var enrichedCount = 0;
            for (var i = 0; i < summaries.Count && enrichedCount < ConversationSummaryEnrichmentLimit; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var summary = summaries[i];
                if (summary == null || !string.IsNullOrWhiteSpace(summary.Preview))
                {
                    continue;
                }

                AppServerConversationSummaryRequest request = null;
                if (!string.IsNullOrWhiteSpace(summary.ThreadId) &&
                    appServerThreadIds != null &&
                    appServerThreadIds.Contains(summary.ThreadId))
                {
                    request = AppServerConversationSummaryRequest.FromConversationId(summary.ThreadId);
                }
                else if (!string.IsNullOrWhiteSpace(summary.SessionPath))
                {
                    request = AppServerConversationSummaryRequest.FromRolloutPath(summary.SessionPath);
                }

                if (request == null)
                {
                    continue;
                }

                enrichedCount++;
                try
                {
                    var officialSummary = await _getConversationSummaryAsync(request, cancellationToken).ConfigureAwait(false);
                    if (officialSummary != null)
                    {
                        AppServerConversationSummaryMapper.ToThreadSummary(officialSummary, summary);
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Trace.TraceWarning(
                        "Failed to enrich conversation summary for thread {0}: {1}",
                        summary.ThreadId ?? "(unknown)",
                        AbbreviateForLog(ex.Message, 500));
                }
            }
        }

        private Task<IDictionary<string, object>> SendRequestAsync(string method, object parameters)
        {
            return SendRequestAsync(method, parameters, CancellationToken.None, DefaultRequestTimeoutMs);
        }

        private Task<IDictionary<string, object>> SendRequestAsync(
            string method,
            object parameters,
            CancellationToken cancellationToken,
            int timeoutMs = DefaultRequestTimeoutMs)
        {
            return _sendRequestAsync(method, parameters, cancellationToken, timeoutMs);
        }

        private void EnsureSupported(Func<AppServerProtocolCapabilities, bool> isSupported, string capabilityName)
        {
            var capabilities = _getProtocolCapabilities();
            AppServerProtocolCapabilityGuards.EnsureSupported(
                capabilities,
                capabilities == null || isSupported(capabilities),
                capabilityName);
        }

        private static void EnsureThreadId(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                throw new ArgumentException(L("AppServerClient.Error.ThreadIdRequired", "Thread ID cannot be empty."), nameof(threadId));
            }
        }

        private static void AddLoadedThreadIds(
            IEnumerable<string> threadIds,
            List<string> result,
            ISet<string> seen,
            int? limit)
        {
            if (threadIds == null)
            {
                return;
            }

            foreach (var threadId in threadIds)
            {
                if (limit.HasValue && limit.Value > 0 && result.Count >= limit.Value)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(threadId) ||
                    !seen.Add(threadId))
                {
                    continue;
                }

                result.Add(threadId);
            }
        }

        private static void AddOptionalMetadataPatch(IDictionary<string, object> parameters, string key, bool includeValue, string value)
        {
            if (!includeValue)
            {
                return;
            }

            parameters[key] = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static void AddOptionalString(IDictionary<string, object> target, string key, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            target[key] = value;
        }

        private static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetDictionary(source, key);
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetString(source, key);
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetArray(source, key);
        }

        private static string AbbreviateForLog(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || maxLength <= 0 || text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength) + "...";
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }
    }
}
