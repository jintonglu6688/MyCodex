using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class AppServerCodingRemoteSessionCreator : ICodingRemoteSessionCreator
    {
        private readonly IAppStateStore _appStateStore;
        private readonly SessionService _sessionService;
        private readonly AppInteractionMode _interactionMode;
        private readonly WorkspaceKind _workspaceKind;

        public AppServerCodingRemoteSessionCreator(IAppStateStore appStateStore)
            : this(appStateStore, new SessionService(), AppInteractionMode.Coding)
        {
        }

        public AppServerCodingRemoteSessionCreator(IAppStateStore appStateStore, AppInteractionMode interactionMode)
            : this(appStateStore, new SessionService(), interactionMode)
        {
        }

        internal AppServerCodingRemoteSessionCreator(IAppStateStore appStateStore, SessionService sessionService)
            : this(appStateStore, sessionService, AppInteractionMode.Coding)
        {
        }

        internal AppServerCodingRemoteSessionCreator(
            IAppStateStore appStateStore,
            SessionService sessionService,
            AppInteractionMode interactionMode)
        {
            _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            _interactionMode = interactionMode;
            _workspaceKind = interactionMode == AppInteractionMode.Office ? WorkspaceKind.Office : WorkspaceKind.Coding;
        }

        public Task<CodingRemoteSessionSummary> CreateSessionAsync(CodingRemoteSessionCreateRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new CodingRemoteSessionCreateRequest();
            if (string.IsNullOrWhiteSpace(request.WorkspaceId))
            {
                throw new InvalidOperationException("Workspace ID is required.");
            }

            var snapshot = _appStateStore.Load() ?? new AppStateSnapshot();
            EnsureLists(snapshot);

            var workspace = FindWorkspace(snapshot, request.WorkspaceId);
            if (workspace == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.WorkspaceNotFound, "Workspace was not found.");
            }

            var workspaceSessions = snapshot.Sessions
                .Where(session => IsSessionForWorkspace(session, workspace.Id))
                .ToList();
            var sessionIndex = workspaceSessions.Count + 1;
            var title = BuildRequestedSessionTitle(request.Title, workspace, sessionIndex, workspaceSessions);
            var session = _sessionService.CreateSession(workspace, sessionIndex, title);
            session.RuntimeProfileId = ResolveRuntimeProfileId(workspaceSessions, snapshot.RuntimeProfiles);

            snapshot.Sessions.Insert(0, session);
            SelectSession(snapshot, workspace.Id, session.Id, session.RuntimeProfileId);
            _appStateStore.Save(snapshot);

            return Task.FromResult(MapSession(session));
        }

        private string BuildRequestedSessionTitle(string requestedTitle, WorkspaceInfo workspace, int sessionIndex, IReadOnlyList<ChatSession> workspaceSessions)
        {
            if (!string.IsNullOrWhiteSpace(requestedTitle))
            {
                return requestedTitle.Trim();
            }

            return BuildUniqueSessionTitle(_sessionService.BuildDefaultSessionTitle(workspace, sessionIndex), workspaceSessions);
        }

        private void SelectSession(AppStateSnapshot snapshot, string workspaceId, string sessionId, string runtimeProfileId)
        {
            snapshot.CurrentInteractionMode = _interactionMode;
            snapshot.CurrentWorkspaceId = workspaceId;
            snapshot.CurrentSessionId = sessionId;
            if (_interactionMode == AppInteractionMode.Office)
            {
                snapshot.CurrentOfficeWorkspaceId = workspaceId;
                snapshot.CurrentOfficeSessionId = sessionId;
                return;
            }

            snapshot.CurrentCodingWorkspaceId = workspaceId;
            snapshot.CurrentCodingSessionId = sessionId;

            if (snapshot.RuntimeProfiles == null || string.IsNullOrWhiteSpace(runtimeProfileId))
            {
                return;
            }

            foreach (var profile in snapshot.RuntimeProfiles)
            {
                if (profile == null ||
                    string.IsNullOrWhiteSpace(profile.RuntimeProfileId) ||
                    !string.Equals(profile.RuntimeProfileId, runtimeProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                profile.CurrentWorkspaceId = workspaceId;
                profile.CurrentSessionId = sessionId;
                return;
            }
        }

        private static CodingRemoteSessionSummary MapSession(ChatSession session)
        {
            return new CodingRemoteSessionSummary
            {
                SessionId = session.Id,
                WorkspaceId = session.WorkspaceId,
                Title = string.IsNullOrWhiteSpace(session.Title) ? "Untitled session" : session.Title.Trim(),
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.LastActiveAt == default(DateTime) ? session.CreatedAt : session.LastActiveAt,
                BranchKind = CodingRemoteSessionBranchKinds.Mainline,
                SubAgentCount = 0,
                SubAgents = new List<CodingRemoteSessionSummary>(),
                LastMessagePreview = session.Preview,
                State = CodingRemoteSessionStates.Idle
            };
        }

        private static string BuildUniqueSessionTitle(string requestedTitle, IEnumerable<ChatSession> sessions)
        {
            var baseTitle = string.IsNullOrWhiteSpace(requestedTitle) ? "Untitled session" : requestedTitle.Trim();
            var existingTitles = new HashSet<string>(
                (sessions ?? Enumerable.Empty<ChatSession>())
                    .Where(session => session != null && !string.IsNullOrWhiteSpace(session.Title))
                    .Select(session => session.Title.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (!existingTitles.Contains(baseTitle))
            {
                return baseTitle;
            }

            for (var index = 2; index < 10000; index++)
            {
                var candidate = string.Format("{0} ({1})", baseTitle, index);
                if (!existingTitles.Contains(candidate))
                {
                    return candidate;
                }
            }

            return string.Format("{0} ({1})", baseTitle, DateTime.Now.Ticks);
        }

        private static string ResolveRuntimeProfileId(IEnumerable<ChatSession> sessions, IEnumerable<RuntimeProfileStateSnapshot> profiles)
        {
            if (sessions != null)
            {
                foreach (var session in sessions)
                {
                    if (session != null && !string.IsNullOrWhiteSpace(session.RuntimeProfileId))
                    {
                        return session.RuntimeProfileId;
                    }
                }
            }

            if (profiles != null)
            {
                foreach (var profile in profiles)
                {
                    if (profile != null && !string.IsNullOrWhiteSpace(profile.RuntimeProfileId))
                    {
                        return profile.RuntimeProfileId;
                    }
                }
            }

            return null;
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

            if (snapshot.RuntimeProfiles == null)
            {
                snapshot.RuntimeProfiles = new List<RuntimeProfileStateSnapshot>();
            }
        }

        private WorkspaceInfo FindWorkspace(AppStateSnapshot snapshot, string workspaceId)
        {
            return snapshot.RecentWorkspaces.FirstOrDefault(workspace =>
                workspace != null &&
                workspace.Kind == _workspaceKind &&
                string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsSessionForWorkspace(ChatSession session, string workspaceId)
        {
            return session != null &&
                   session.Mode == _interactionMode &&
                   string.Equals(session.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
