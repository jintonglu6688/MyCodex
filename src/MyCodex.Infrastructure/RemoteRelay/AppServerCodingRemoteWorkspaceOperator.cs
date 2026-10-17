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
    public sealed class AppServerCodingRemoteWorkspaceOperator : ICodingRemoteWorkspaceOperator
    {
        private readonly IAppStateStore _appStateStore;
        private readonly IAppServerThreadClient _threadClient;

        public AppServerCodingRemoteWorkspaceOperator(IAppStateStore appStateStore, IAppServerThreadClient threadClient)
        {
            _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
            _threadClient = threadClient ?? throw new ArgumentNullException(nameof(threadClient));
        }

        public async Task<CodingRemoteWorkspaceOperationResult> OperateWorkspaceAsync(CodingRemoteWorkspaceOperationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new CodingRemoteWorkspaceOperationRequest();
            if (string.IsNullOrWhiteSpace(request.WorkspaceId))
            {
                throw new InvalidOperationException("Workspace ID is required.");
            }

            var snapshot = _appStateStore.Load() ?? new AppStateSnapshot();
            EnsureLists(snapshot);

            var workspace = FindCodingWorkspace(snapshot, request.WorkspaceId);
            if (workspace == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.WorkspaceNotFound, "Workspace was not found.");
            }

            var sessions = snapshot.Sessions
                .Where(session => IsCodingSessionForWorkspace(session, workspace.Id))
                .ToList();
            var processedCount = 0;
            SessionArchiveResult archiveResult = null;
            if (string.Equals(request.Action, CodingRemoteWorkspaceActions.Archive, StringComparison.OrdinalIgnoreCase))
            {
                if (!sessions.Any(session => !string.IsNullOrWhiteSpace(session.ThreadId)))
                    throw new CodingRemoteOperationException(RemoteErrorCodes.SessionNotFound, "No archivable sessions in current workspace.");
                archiveResult = await SessionArchiveService.ArchiveAsync(_threadClient, sessions, cancellationToken).ConfigureAwait(false);
                var confirmed = sessions.Where(session => archiveResult.ArchivedThreadIds.Contains(session.ThreadId ?? string.Empty)).ToList();
                processedCount = confirmed.Count;
                AppServerCodingRemoteSessionOperator.RemoveSessionState(snapshot, confirmed);
            }
            else if (string.Equals(request.Action, CodingRemoteWorkspaceActions.Delete, StringComparison.OrdinalIgnoreCase))
            {
                processedCount = request.DeleteLocalHistory
                    ? await DeleteWorkspaceSessionFilesAsync(sessions, cancellationToken).ConfigureAwait(false)
                    : sessions.Count;
            }
            else
            {
                throw new InvalidOperationException("Unsupported workspace operation.");
            }

            var removed = archiveResult == null || !snapshot.Sessions.Any(session => IsCodingSessionForWorkspace(session, workspace.Id));
            if (removed) RemoveWorkspaceState(snapshot, workspace.Id);
            _appStateStore.Save(snapshot);

            if (archiveResult != null && archiveResult.Failures.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine,
                    archiveResult.Failures.Select(pair => pair.Key + ": " + pair.Value)));

            return new CodingRemoteWorkspaceOperationResult
            {
                WorkspaceId = workspace.Id,
                Action = request.Action,
                RuntimeProfileId = ResolveRuntimeProfileId(sessions),
                DeleteLocalHistory = request.DeleteLocalHistory,
                ProcessedCount = processedCount,
                Removed = removed,
                AffectedSessionIds = sessions
                    .Where(session => archiveResult == null || archiveResult.ArchivedThreadIds.Contains(session.ThreadId ?? string.Empty))
                    .Where(session => session != null && !string.IsNullOrWhiteSpace(session.Id))
                    .Select(session => session.Id)
                    .ToList(),
                StatusText = BuildStatusText(workspace.Name, request.Action, request.DeleteLocalHistory, processedCount)
            };
        }

        private async Task<int> DeleteWorkspaceSessionFilesAsync(IReadOnlyList<ChatSession> sessions, CancellationToken cancellationToken)
        {
            var targets = sessions.Where(session => session != null).ToList();
            if (targets.Count == 0)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.SessionNotFound, "No session files available for permanent deletion in current workspace.");
            }

            foreach (var session in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeleteSessionFileAsync(session, cancellationToken).ConfigureAwait(false);
            }

            return targets.Count;
        }

        private async Task DeleteSessionFileAsync(ChatSession session, CancellationToken cancellationToken)
        {
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

        private static void RemoveWorkspaceState(AppStateSnapshot snapshot, string workspaceId)
        {
            snapshot.RecentWorkspaces = snapshot.RecentWorkspaces
                .Where(workspace => workspace != null && !string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var removedSessionIds = new HashSet<string>(
                snapshot.Sessions
                    .Where(session => IsCodingSessionForWorkspace(session, workspaceId))
                    .Select(session => session.Id),
                StringComparer.OrdinalIgnoreCase);
            snapshot.Sessions = snapshot.Sessions
                .Where(session => session == null || !removedSessionIds.Contains(session.Id))
                .ToList();

            ClearSelection(snapshot, workspaceId, removedSessionIds);
            if (snapshot.RuntimeProfiles == null)
            {
                return;
            }

            foreach (var profile in snapshot.RuntimeProfiles)
            {
                if (profile == null)
                {
                    continue;
                }

                if (profile.RecentWorkspaces != null)
                {
                    profile.RecentWorkspaces = profile.RecentWorkspaces
                        .Where(workspace => workspace != null && !string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (string.Equals(profile.CurrentWorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    profile.CurrentWorkspaceId = null;
                }

                if (!string.IsNullOrWhiteSpace(profile.CurrentSessionId) &&
                    removedSessionIds.Contains(profile.CurrentSessionId))
                {
                    profile.CurrentSessionId = null;
                }
            }
        }

        private static void ClearSelection(AppStateSnapshot snapshot, string workspaceId, HashSet<string> removedSessionIds)
        {
            if (string.Equals(snapshot.CurrentWorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            {
                snapshot.CurrentWorkspaceId = null;
            }

            if (string.Equals(snapshot.CurrentCodingWorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            {
                snapshot.CurrentCodingWorkspaceId = null;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.CurrentSessionId) &&
                removedSessionIds.Contains(snapshot.CurrentSessionId))
            {
                snapshot.CurrentSessionId = null;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.CurrentCodingSessionId) &&
                removedSessionIds.Contains(snapshot.CurrentCodingSessionId))
            {
                snapshot.CurrentCodingSessionId = null;
            }
        }

        private static void EnsureLists(AppStateSnapshot snapshot)
        {
            if (snapshot.RecentWorkspaces == null)
            {
                snapshot.RecentWorkspaces = new List<WorkspaceInfo>();
            }

            if (snapshot.Sessions == null)
            {
                snapshot.Sessions = new List<ChatSession>();
            }
        }

        private static WorkspaceInfo FindCodingWorkspace(AppStateSnapshot snapshot, string workspaceId)
        {
            return snapshot.RecentWorkspaces.FirstOrDefault(workspace =>
                workspace != null &&
                workspace.Kind == WorkspaceKind.Coding &&
                string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsCodingSessionForWorkspace(ChatSession session, string workspaceId)
        {
            return session != null &&
                   session.Mode == AppInteractionMode.Coding &&
                   string.Equals(session.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase);
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

        private static string ResolveRuntimeProfileId(IEnumerable<ChatSession> sessions)
        {
            if (sessions == null)
            {
                return null;
            }

            foreach (var session in sessions)
            {
                if (session != null && !string.IsNullOrWhiteSpace(session.RuntimeProfileId))
                {
                    return session.RuntimeProfileId;
                }
            }

            return null;
        }

        private static string BuildStatusText(string workspaceName, string action, bool deleteLocalHistory, int processedCount)
        {
            if (string.Equals(action, CodingRemoteWorkspaceActions.Archive, StringComparison.OrdinalIgnoreCase))
            {
                return string.Format("Archived {0} session(s) under workspace \"{1}\".", processedCount, workspaceName);
            }

            return deleteLocalHistory
                ? string.Format("Permanently deleted {0} session file(s) under workspace \"{1}\".", processedCount, workspaceName)
                : string.Format("Removed workspace \"{0}\" from MyCodex.", workspaceName);
        }
    }
}
