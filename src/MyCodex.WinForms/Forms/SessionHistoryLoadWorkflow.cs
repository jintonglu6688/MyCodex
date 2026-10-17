using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal enum SessionHistoryLoadResultKind
    {
        None,
        DraftSession,
        CachedHistory,
        LoadingRemoteHistory,
        LoadedRemoteHistory,
        MissingThreadRecoveredAsDraft,
        MissingThread,
        ConnectivityUnavailable,
        Error,
        Stale
    }

    internal enum SessionHistoryLoadStatusPriority
    {
        None,
        Normal,
        High
    }

    internal sealed class SessionHistoryLoadResult
    {
        public SessionHistoryLoadResult(
            SessionHistoryLoadResultKind kind,
            ChatHistoryItem[] historyItems,
            string statusText,
            SessionHistoryLoadStatusPriority statusPriority)
        {
            Kind = kind;
            HistoryItems = historyItems;
            StatusText = statusText ?? string.Empty;
            StatusPriority = statusPriority;
        }

        public SessionHistoryLoadResultKind Kind { get; }

        internal AppServerThreadDetails ThreadDetails { get; set; }

        public ChatHistoryItem[] HistoryItems { get; }

        public string StatusText { get; }

        public SessionHistoryLoadStatusPriority StatusPriority { get; }

        public bool ShouldReplaceHistory
        {
            get { return HistoryItems != null; }
        }

        public bool HasStatus
        {
            get { return StatusPriority != SessionHistoryLoadStatusPriority.None && !string.IsNullOrWhiteSpace(StatusText); }
        }
    }

    internal sealed class SessionHistoryLoadRequest
    {
        internal SessionHistoryLoadRequest(
            ChatSession session,
            bool includeSystemHeader,
            string threadId,
            int version,
            bool requiresRemoteRead,
            SessionHistoryLoadResult immediateResult)
        {
            Session = session;
            IncludeSystemHeader = includeSystemHeader;
            ThreadId = threadId;
            Version = version;
            RequiresRemoteRead = requiresRemoteRead;
            ImmediateResult = immediateResult;
        }

        public ChatSession Session { get; }

        public bool IncludeSystemHeader { get; }

        public string ThreadId { get; }

        public int Version { get; }

        public bool RequiresRemoteRead { get; }

        public SessionHistoryLoadResult ImmediateResult { get; }
    }

    internal sealed class SessionHistoryLoadWorkflow
    {
        private readonly SessionHistoryStateStore _stateStore;
        private readonly SessionHistoryDisplayBuilder _displayBuilder;
        private readonly Func<string, Task<AppServerThreadDetails>> _readThreadAsync;
        private readonly Func<Exception, bool> _isMissingThreadRolloutError;
        private readonly Func<Exception, bool> _isConnectivityFailure;
        private readonly Dictionary<string, InFlightThreadRead> _inFlightReads =
            new Dictionary<string, InFlightThreadRead>(StringComparer.OrdinalIgnoreCase);
        private int _loadVersion;

        internal SessionHistoryLoadWorkflow(
            SessionHistoryStateStore stateStore,
            SessionHistoryDisplayBuilder displayBuilder,
            Func<string, Task<AppServerThreadDetails>> readThreadAsync,
            Func<Exception, bool> isMissingThreadRolloutError,
            Func<Exception, bool> isConnectivityFailure)
        {
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _displayBuilder = displayBuilder ?? throw new ArgumentNullException(nameof(displayBuilder));
            _readThreadAsync = readThreadAsync ?? throw new ArgumentNullException(nameof(readThreadAsync));
            _isMissingThreadRolloutError = isMissingThreadRolloutError ?? throw new ArgumentNullException(nameof(isMissingThreadRolloutError));
            _isConnectivityFailure = isConnectivityFailure ?? throw new ArgumentNullException(nameof(isConnectivityFailure));
        }

        internal SessionHistoryLoadRequest BuildRequest(ChatSession session, bool includeSystemHeader)
        {
            if (session == null)
            {
                return new SessionHistoryLoadRequest(
                    null,
                    includeSystemHeader,
                    null,
                    _loadVersion,
                    false,
                    new SessionHistoryLoadResult(SessionHistoryLoadResultKind.None, null, null, SessionHistoryLoadStatusPriority.None));
            }

            if (string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return new SessionHistoryLoadRequest(
                    session,
                    includeSystemHeader,
                    null,
                    _loadVersion,
                    false,
                    new SessionHistoryLoadResult(
                        SessionHistoryLoadResultKind.DraftSession,
                        _displayBuilder.BuildDraftSessionHistoryItems(session, includeSystemHeader),
                        null,
                        SessionHistoryLoadStatusPriority.None));
            }

            ChatHistoryItem[] cachedHistoryItems;
            if (_stateStore.TryGetCached(session.ThreadId, out cachedHistoryItems))
            {
                TraceLoad("cache-hit", session.ThreadId, "HistoryItems={0}", cachedHistoryItems != null ? cachedHistoryItems.Length : 0);
                _stateStore.ClearPendingReload(session.ThreadId);
                return new SessionHistoryLoadRequest(
                    session,
                    includeSystemHeader,
                    session.ThreadId,
                    _loadVersion,
                    false,
                    new SessionHistoryLoadResult(
                        SessionHistoryLoadResultKind.CachedHistory,
                        _displayBuilder.BuildDisplayChatHistoryItems(session, includeSystemHeader, cachedHistoryItems),
                        null,
                        SessionHistoryLoadStatusPriority.None));
            }

            var version = ++_loadVersion;
            TraceLoad("load-start", session.ThreadId, "LoadVersion={0}", version);
            return new SessionHistoryLoadRequest(
                session,
                includeSystemHeader,
                session.ThreadId,
                version,
                true,
                new SessionHistoryLoadResult(
                    SessionHistoryLoadResultKind.LoadingRemoteHistory,
                    _displayBuilder.BuildLoadingChatHistoryItems(session, includeSystemHeader),
                    UiLocalization.Text("MainForm.Status.LoadingSessionHistory", "Loading session history..."),
                    SessionHistoryLoadStatusPriority.Normal));
        }

        internal void Invalidate(string threadId)
        {
            _stateStore.Invalidate(threadId);
        }

        internal async Task<SessionHistoryLoadResult> CompleteRemoteLoadAsync(
            SessionHistoryLoadRequest request,
            Func<ChatSession, string, bool> isCurrentLoadTarget,
            Func<ChatSession, bool> tryRecoverMissingThreadAsDraft,
            Func<Task> beforeBuildResultAsync = null)
        {
            if (request == null || !request.RequiresRemoteRead)
            {
                return new SessionHistoryLoadResult(SessionHistoryLoadResultKind.None, null, null, SessionHistoryLoadStatusPriority.None);
            }

            var watch = Stopwatch.StartNew();
            AppServerThreadDetails details;
            ChatHistoryItem[] historyItems;
            try
            {
                var read = await GetOrStartRead(request.ThreadId);
                details = read.Details;
                historyItems = read.HistoryItems;
                TraceLoad("remote-read", request.ThreadId, "LoadVersion={0}; ElapsedMs={1}", request.Version, watch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(BuildTraceText("remote-failed", request.ThreadId,
                    "LoadVersion={0}; ElapsedMs={1}; ErrorType={2}; Message={3}",
                    request.Version, watch.ElapsedMilliseconds, ex.GetType().FullName, AbbreviateForLog(ex.Message)));
                if (IsStale(request, isCurrentLoadTarget))
                {
                    TraceLoad("stale", request.ThreadId, "LoadVersion={0}; ElapsedMs={1}", request.Version, watch.ElapsedMilliseconds);
                    return BuildStaleResult();
                }

                await InvokeBeforeBuildResultAsync(beforeBuildResultAsync, request);
                if (IsStale(request, isCurrentLoadTarget)) return BuildStaleResult();
                if (_isMissingThreadRolloutError(ex))
                {
                    if (tryRecoverMissingThreadAsDraft != null && tryRecoverMissingThreadAsDraft(request.Session))
                    {
                        return new SessionHistoryLoadResult(
                            SessionHistoryLoadResultKind.MissingThreadRecoveredAsDraft,
                            _displayBuilder.BuildDraftSessionHistoryItems(request.Session, request.IncludeSystemHeader),
                            UiLocalization.Text("MainForm.Status.EmptySessionRecoveredAsDraft", "Recovered the current empty session as a local draft"),
                            SessionHistoryLoadStatusPriority.Normal);
                    }

                    _stateStore.Invalidate(request.ThreadId);
                    _stateStore.ClearPendingReload(request.ThreadId);
                    return new SessionHistoryLoadResult(
                        SessionHistoryLoadResultKind.MissingThread,
                        _displayBuilder.BuildMissingThreadHistoryItems(request.Session, request.IncludeSystemHeader, ex.Message),
                        UiLocalization.Text("MainForm.Status.SessionThreadMissingWillRecreateOnSend", "Current session thread is missing and will be recreated when sending"),
                        SessionHistoryLoadStatusPriority.Normal);
                }

                if (_isConnectivityFailure(ex))
                {
                    _stateStore.MarkPendingReload(request.ThreadId);
                    return new SessionHistoryLoadResult(
                        SessionHistoryLoadResultKind.ConnectivityUnavailable,
                        _displayBuilder.BuildConnectivityUnavailableHistoryItems(request.Session, request.IncludeSystemHeader, ex.Message),
                        UiLocalization.Text("MainForm.Status.AppServerDisconnectedPressEnterToRetry", "App Server is not connected. Press Enter to retry"),
                        SessionHistoryLoadStatusPriority.High);
                }

                return new SessionHistoryLoadResult(
                    SessionHistoryLoadResultKind.Error,
                    _displayBuilder.BuildErrorChatHistoryItems(request.Session, request.IncludeSystemHeader, ex.Message),
                    UiLocalization.Text("MainForm.Status.LoadHistoryFailed", "Failed to load history"),
                    SessionHistoryLoadStatusPriority.High);
            }

            if (IsStale(request, isCurrentLoadTarget))
            {
                TraceLoad("stale", request.ThreadId, "LoadVersion={0}; ElapsedMs={1}", request.Version, watch.ElapsedMilliseconds);
                return BuildStaleResult();
            }

            await InvokeBeforeBuildResultAsync(beforeBuildResultAsync, request);
            if (IsStale(request, isCurrentLoadTarget))
            {
                TraceLoad("stale", request.ThreadId, "LoadVersion={0}; ElapsedMs={1}", request.Version, watch.ElapsedMilliseconds);
                return BuildStaleResult();
            }

            if (details != null && !string.IsNullOrWhiteSpace(details.SessionPath))
            {
                request.Session.SessionPath = details.SessionPath;
            }

            var buildWatch = Stopwatch.StartNew();
            _stateStore.ClearPendingReload(request.ThreadId);
            var displayItems = _displayBuilder.BuildDisplayChatHistoryItems(request.Session, request.IncludeSystemHeader, historyItems);
            TraceLoad("build-items", request.ThreadId, "LoadVersion={0}; HistoryItems={1}; DisplayItems={2}; ElapsedMs={3}; TotalElapsedMs={4}",
                request.Version, historyItems.Length, displayItems != null ? displayItems.Length : 0,
                buildWatch.ElapsedMilliseconds, watch.ElapsedMilliseconds);
            return new SessionHistoryLoadResult(
                SessionHistoryLoadResultKind.LoadedRemoteHistory,
                displayItems,
                null,
                SessionHistoryLoadStatusPriority.None) { ThreadDetails = details };
        }

        // Long histories can take minutes to read. Reloads of the same thread join the read in
        // progress instead of starting another, and a completed read is cached even when the load
        // that started it was superseded, unless the history was invalidated meanwhile.
        private Task<CompletedThreadRead> GetOrStartRead(string threadId)
        {
            var generation = _stateStore.GetGeneration(threadId);
            InFlightThreadRead existing;
            if (_inFlightReads.TryGetValue(threadId, out existing) &&
                existing.Generation == generation &&
                !existing.Task.IsCompleted)
            {
                TraceLoad("join-read", threadId, "Generation={0}", generation);
                return existing.Task;
            }

            var task = ReadAndCacheAsync(threadId, generation);
            if (!task.IsCompleted)
            {
                _inFlightReads[threadId] = new InFlightThreadRead(generation, task);
            }

            return task;
        }

        private async Task<CompletedThreadRead> ReadAndCacheAsync(string threadId, int generation)
        {
            try
            {
                var details = await _readThreadAsync(threadId);
                var historyItems = _displayBuilder.BuildHistoryChatItems(details);
                var cached = _stateStore.TryStore(threadId, generation, historyItems);
                TraceLoad("read-cached", threadId, "Generation={0}; Cached={1}; HistoryItems={2}", generation, cached, historyItems.Length);
                return new CompletedThreadRead(details, historyItems);
            }
            finally
            {
                InFlightThreadRead current;
                if (_inFlightReads.TryGetValue(threadId, out current) && current.Generation == generation)
                {
                    _inFlightReads.Remove(threadId);
                }
            }
        }

        private bool IsStale(SessionHistoryLoadRequest request, Func<ChatSession, string, bool> isCurrentLoadTarget)
        {
            if (request.Version != _loadVersion)
            {
                return true;
            }

            return isCurrentLoadTarget != null && !isCurrentLoadTarget(request.Session, request.ThreadId);
        }

        private static SessionHistoryLoadResult BuildStaleResult()
        {
            return new SessionHistoryLoadResult(SessionHistoryLoadResultKind.Stale, null, null, SessionHistoryLoadStatusPriority.None);
        }

        private static async Task InvokeBeforeBuildResultAsync(Func<Task> beforeBuildResultAsync, SessionHistoryLoadRequest request)
        {
            if (beforeBuildResultAsync == null)
            {
                return;
            }

            // Goal refresh and target-exit decisions can hold the result after the read completes.
            var watch = Stopwatch.StartNew();
            await beforeBuildResultAsync();
            TraceLoad("wait-before-build", request.ThreadId, "LoadVersion={0}; ElapsedMs={1}", request.Version, watch.ElapsedMilliseconds);
        }

        internal static void TraceLoad(string phase, string threadId, string format, params object[] args)
        {
            Trace.TraceInformation(BuildTraceText(phase, threadId, format, args));
        }

        internal static string BuildTraceText(string phase, string threadId, string format, params object[] args)
        {
            return "[HistoryLoadTiming] Phase=" + phase + "; ThreadId=" + (threadId ?? string.Empty) + "; " +
                string.Format(CultureInfo.InvariantCulture, format ?? string.Empty, args ?? new object[0]);
        }

        private static string AbbreviateForLog(string text)
        {
            text = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
            return text.Length <= 300 ? text : text.Substring(0, 300) + "...";
        }

        private sealed class InFlightThreadRead
        {
            internal InFlightThreadRead(int generation, Task<CompletedThreadRead> task)
            {
                Generation = generation;
                Task = task;
            }

            internal int Generation { get; }

            internal Task<CompletedThreadRead> Task { get; }
        }

        private sealed class CompletedThreadRead
        {
            internal CompletedThreadRead(AppServerThreadDetails details, ChatHistoryItem[] historyItems)
            {
                Details = details;
                HistoryItems = historyItems;
            }

            internal AppServerThreadDetails Details { get; }

            internal ChatHistoryItem[] HistoryItems { get; }
        }
    }
}
