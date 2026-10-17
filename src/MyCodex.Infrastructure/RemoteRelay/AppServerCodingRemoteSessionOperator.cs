using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class AppServerCodingRemoteSessionOperator : ICodingRemoteSessionOperator
    {
        private readonly IAppStateStore _appStateStore;
        private readonly IAppServerThreadClient _threadClient;
        private readonly AppInteractionMode _interactionMode;

        public AppServerCodingRemoteSessionOperator(IAppStateStore appStateStore, IAppServerThreadClient threadClient)
            : this(appStateStore, threadClient, AppInteractionMode.Coding)
        {
        }

        public AppServerCodingRemoteSessionOperator(
            IAppStateStore appStateStore,
            IAppServerThreadClient threadClient,
            AppInteractionMode interactionMode)
        {
            _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
            _threadClient = threadClient ?? throw new ArgumentNullException(nameof(threadClient));
            _interactionMode = interactionMode;
        }

        public async Task<CodingRemoteSessionOperationResult> OperateSessionAsync(CodingRemoteSessionOperationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new CodingRemoteSessionOperationRequest();
            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                throw new InvalidOperationException("Session ID is required.");
            }

            var snapshot = _appStateStore.Load() ?? new AppStateSnapshot();
            EnsureLists(snapshot);

            var session = FindSession(snapshot, request.SessionId);
            if (session == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.SessionNotFound, "Session was not found.");
            }

            if (string.Equals(request.Action, CodingRemoteSessionActions.Rename, StringComparison.OrdinalIgnoreCase))
            {
                return await RenameSessionAsync(snapshot, session, request, cancellationToken).ConfigureAwait(false);
            }

            var targets = SessionDescendantCollector.Collect(session, snapshot.Sessions);
            SessionArchiveResult archiveResult = null;
            if (string.Equals(request.Action, CodingRemoteSessionActions.Archive, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(session.ThreadId))
                    throw new CodingRemoteOperationException(RemoteErrorCodes.SessionNotFound, "Current session has no archivable thread ID.");
                archiveResult = await SessionArchiveService.ArchiveAsync(_threadClient, targets, cancellationToken).ConfigureAwait(false);
                targets = targets.Where(target => archiveResult.ArchivedThreadIds.Contains(target.ThreadId ?? string.Empty)).ToList();
            }
            else if (string.Equals(request.Action, CodingRemoteSessionActions.Delete, StringComparison.OrdinalIgnoreCase))
            {
                if (request.DeleteLocalHistory)
                {
                    foreach (var target in targets)
                    {
                        await DeleteSessionFileAsync(target, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                throw new InvalidOperationException("Unsupported session operation.");
            }

            RemoveSessionState(snapshot, targets);
            _appStateStore.Save(snapshot);

            if (archiveResult != null && archiveResult.Failures.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine,
                    archiveResult.Failures.Select(pair => pair.Key + ": " + pair.Value)));

            return new CodingRemoteSessionOperationResult
            {
                SessionId = session.Id,
                WorkspaceId = session.WorkspaceId,
                Action = request.Action,
                RuntimeProfileId = session.RuntimeProfileId,
                DeleteLocalHistory = request.DeleteLocalHistory,
                ProcessedCount = targets.Count,
                Removed = true,
                StatusText = BuildStatusText(session.Title, request.Action, request.DeleteLocalHistory)
            };
        }

        private async Task<CodingRemoteSessionOperationResult> RenameSessionAsync(
            AppStateSnapshot snapshot,
            ChatSession session,
            CodingRemoteSessionOperationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var title = string.IsNullOrWhiteSpace(request.Title) ? string.Empty : request.Title.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new InvalidOperationException("Session title is required.");
            }

            var currentTitle = string.IsNullOrWhiteSpace(session.Title) ? string.Empty : session.Title.Trim();
            string remoteSyncError = null;
            if (!string.Equals(currentTitle, title, StringComparison.Ordinal))
            {
                session.Title = title;
                _appStateStore.Save(snapshot);

                if (!string.IsNullOrWhiteSpace(session.ThreadId))
                {
                    try
                    {
                        await _threadClient.SetThreadNameAsync(session.ThreadId, title).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        remoteSyncError = ex.Message;
                    }
                }
            }

            return new CodingRemoteSessionOperationResult
            {
                SessionId = session.Id,
                WorkspaceId = session.WorkspaceId,
                Action = CodingRemoteSessionActions.Rename,
                RuntimeProfileId = session.RuntimeProfileId,
                ProcessedCount = 1,
                Removed = false,
                Updated = true,
                Title = title,
                StatusText = string.IsNullOrWhiteSpace(remoteSyncError)
                    ? BuildStatusText(title, CodingRemoteSessionActions.Rename, false)
                    : string.Format("Renamed session \"{0}\" locally; remote sync failed.", title)
            };
        }

        private async Task DeleteSessionFileAsync(ChatSession session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session == null)
            {
                return;
            }

            if (await TryDeleteThreadAsync(session, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(session.ThreadId))
            {
                try
                {
                    await _threadClient.UnsubscribeThreadAsync(session.ThreadId).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            var sessionPath = await ResolveSessionPathAsync(session, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(sessionPath))
            {
                await _threadClient.DeleteThreadFileAsync(sessionPath).ConfigureAwait(false);
                session.SessionPath = sessionPath;
            }
        }

        private async Task<bool> TryDeleteThreadAsync(ChatSession session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return false;
            }

            try
            {
                await _threadClient.DeleteThreadAsync(session.ThreadId).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> ResolveSessionPathAsync(ChatSession session, CancellationToken cancellationToken)
        {
            if (session == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(session.SessionPath))
            {
                return session.SessionPath;
            }

            if (string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return null;
            }

            var threads = await _threadClient.ListThreadsAsync(cancellationToken).ConfigureAwait(false);
            var path = ResolveSessionPathFromThreadSummaries(threads, session.ThreadId);
            if (!string.IsNullOrWhiteSpace(path))
            {
                session.SessionPath = path;
                return path;
            }

            try
            {
                var details = await _threadClient.ReadThreadAsync(session.ThreadId, cancellationToken).ConfigureAwait(false);
                if (details != null && !string.IsNullOrWhiteSpace(details.SessionPath))
                {
                    session.SessionPath = details.SessionPath;
                }
            }
            catch (InvalidOperationException)
            {
            }

            return session.SessionPath;
        }

        internal static void RemoveSessionState(AppStateSnapshot snapshot, IReadOnlyList<ChatSession> targets)
        {
            var removalIds = new HashSet<string>(
                (targets ?? Array.Empty<ChatSession>())
                    .Where(session => session != null && !string.IsNullOrWhiteSpace(session.Id))
                    .Select(session => session.Id),
                StringComparer.OrdinalIgnoreCase);
            var primarySession = targets != null && targets.Count > 0 ? targets[0] : null;

            snapshot.Sessions = snapshot.Sessions
                .Where(item => item == null || string.IsNullOrWhiteSpace(item.Id) || !removalIds.Contains(item.Id))
                .ToList();

            var replacementSessionId = ResolveReplacementSessionId(snapshot, primarySession);
            if (!string.IsNullOrWhiteSpace(snapshot.CurrentSessionId) && removalIds.Contains(snapshot.CurrentSessionId))
            {
                snapshot.CurrentSessionId = replacementSessionId;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.CurrentCodingSessionId) && removalIds.Contains(snapshot.CurrentCodingSessionId))
            {
                snapshot.CurrentCodingSessionId = primarySession != null && primarySession.Mode == AppInteractionMode.Coding
                    ? replacementSessionId
                    : null;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.CurrentOfficeSessionId) && removalIds.Contains(snapshot.CurrentOfficeSessionId))
            {
                snapshot.CurrentOfficeSessionId = primarySession != null && primarySession.Mode == AppInteractionMode.Office
                    ? replacementSessionId
                    : null;
            }

            if (snapshot.RuntimeProfiles == null)
            {
                return;
            }

            foreach (var profile in snapshot.RuntimeProfiles)
            {
                if (profile != null &&
                    !string.IsNullOrWhiteSpace(profile.CurrentSessionId) &&
                    removalIds.Contains(profile.CurrentSessionId))
                {
                    profile.CurrentSessionId = replacementSessionId;
                }
            }
        }

        private static string ResolveReplacementSessionId(AppStateSnapshot snapshot, ChatSession removedSession)
        {
            if (snapshot == null || removedSession == null || snapshot.Sessions == null)
            {
                return null;
            }

            var replacement = snapshot.Sessions
                .Where(session =>
                    session != null &&
                    session.Mode == removedSession.Mode &&
                    string.Equals(session.WorkspaceId, removedSession.WorkspaceId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ResolveUpdatedAt)
                .FirstOrDefault();
            return replacement != null ? replacement.Id : null;
        }

        private static void EnsureLists(AppStateSnapshot snapshot)
        {
            if (snapshot.Sessions == null)
            {
                snapshot.Sessions = new List<ChatSession>();
            }
        }

        private ChatSession FindSession(AppStateSnapshot snapshot, string sessionId)
        {
            return snapshot.Sessions.FirstOrDefault(session =>
                session != null &&
                session.Mode == _interactionMode &&
                string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveSessionPathFromThreadSummaries(IEnumerable<AppServerThreadSummary> threads, string threadId)
        {
            if (threads == null || string.IsNullOrWhiteSpace(threadId))
            {
                return null;
            }

            foreach (var thread in threads)
            {
                if (thread != null &&
                    string.Equals(thread.ThreadId, threadId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(thread.SessionPath))
                {
                    return thread.SessionPath;
                }
            }

            return null;
        }

        private static DateTime ResolveUpdatedAt(ChatSession session)
        {
            if (session == null)
            {
                return default(DateTime);
            }

            return session.LastActiveAt == default(DateTime)
                ? session.CreatedAt
                : session.LastActiveAt;
        }

        private static string BuildStatusText(string sessionTitle, string action, bool deleteLocalHistory)
        {
            if (string.Equals(action, CodingRemoteSessionActions.Archive, StringComparison.OrdinalIgnoreCase))
            {
                return string.Format("Archived session \"{0}\".", sessionTitle);
            }

            if (string.Equals(action, CodingRemoteSessionActions.Rename, StringComparison.OrdinalIgnoreCase))
            {
                return string.Format("Renamed session \"{0}\".", sessionTitle);
            }

            return deleteLocalHistory
                ? string.Format("Permanently deleted session file \"{0}\".", sessionTitle)
                : string.Format("Removed session \"{0}\" from MyCodex.", sessionTitle);
        }
    }
}
