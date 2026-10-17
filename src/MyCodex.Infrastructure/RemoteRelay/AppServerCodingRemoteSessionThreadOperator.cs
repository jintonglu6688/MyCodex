using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class AppServerCodingRemoteSessionThreadOperator : ICodingRemoteSessionThreadOperator
    {
        private readonly IAppStateStore _appStateStore;
        private readonly IAppServerThreadClient _threadClient;

        public AppServerCodingRemoteSessionThreadOperator(IAppStateStore appStateStore, IAppServerThreadClient threadClient)
        {
            if (appStateStore == null)
            {
                throw new ArgumentNullException(nameof(appStateStore));
            }

            if (threadClient == null)
            {
                throw new ArgumentNullException(nameof(threadClient));
            }

            _appStateStore = appStateStore;
            _threadClient = threadClient;
        }

        public async Task<CodingRemoteSessionThreadOperationResult> OperateSessionThreadAsync(
            CodingRemoteSessionThreadOperationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new CodingRemoteSessionThreadOperationRequest();
            var loadedSnapshot = _appStateStore.Load();
            var snapshot = loadedSnapshot ?? new AppStateSnapshot();
            var session = FindCodingSession(snapshot, request.SessionId);
            if (session == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.SessionNotFound, "Session was not found.");
            }

            if (string.Equals(request.Action, CodingRemoteSessionThreadActions.Fork, StringComparison.OrdinalIgnoreCase))
            {
                return await ForkAsync(snapshot, session, request, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(request.Action, CodingRemoteSessionThreadActions.Compact, StringComparison.OrdinalIgnoreCase))
            {
                return await CompactAsync(snapshot, session, cancellationToken).ConfigureAwait(false);
            }

            throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Unsupported session thread operation.");
        }

        private async Task<CodingRemoteSessionThreadOperationResult> ForkAsync(
            AppStateSnapshot snapshot,
            ChatSession sourceSession,
            CodingRemoteSessionThreadOperationRequest request,
            CancellationToken cancellationToken)
        {
            var sourceWorkspace = FindWorkspace(snapshot, sourceSession.WorkspaceId);
            if (sourceWorkspace == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.WorkspaceNotFound, "Workspace was not found.");
            }

            if (!string.IsNullOrWhiteSpace(request.Mode) &&
                !string.Equals(request.Mode, CodingRemoteSessionThreadOperationModes.Full, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.Mode, CodingRemoteSessionThreadOperationModes.FromTurn, StringComparison.OrdinalIgnoreCase))
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Unsupported fork mode.");
            }
            var lastTurnId = string.Equals(request.Mode, CodingRemoteSessionThreadOperationModes.FromTurn, StringComparison.OrdinalIgnoreCase)
                ? request.TurnId : null;
            if (string.Equals(request.Mode, CodingRemoteSessionThreadOperationModes.FromTurn, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(lastTurnId))
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Turn ID is required.");
            }
            var settings = new SessionDefaultsResolution { ModelId = sourceSession.ModelId, ReasoningEffort = sourceSession.ReasoningEffort };
            var configClient = _threadClient as IAppServerConfigClient;
            if (configClient != null)
            {
                var connection = _threadClient as IAppServerConnectionClient;
                var observed = await new AppServerModelConfigurationService(configClient).RefreshAsync(
                    sourceSession.RuntimeProfileId ?? "current", sourceWorkspace.RootPath,
                    connection != null && connection.ProtocolCapabilities != null && connection.ProtocolCapabilities.SupportsConfigRequirements).ConfigureAwait(false);
                var defaults = new SessionDefaultsService().Resolve(null, observed.Models, observed.Requirements, observed.WorkspaceConfig ?? observed.GlobalConfig);
                settings = SessionDefaultsService.ResolveSession(defaults, sourceSession, observed.Models);
            }
            var service = new SessionForkService(_threadClient);
            var forkSummary = await service.ForkAsync(sourceSession, sourceWorkspace, lastTurnId,
                sourceSession.ApprovalMode, settings.ModelId, settings.ReasoningEffort, cancellationToken).ConfigureAwait(false);
            var title = NormalizeTitle(request.Title, sourceSession.Title, "Fork");
            var createdSession = CreateLocalSession(snapshot, sourceWorkspace, sourceSession, forkSummary, title, isFork: true);
            SaveCreatedSession(snapshot, createdSession, request.SwitchToCreatedSession);
            var warning = await service.TrySetNameAsync(forkSummary.ThreadId, title).ConfigureAwait(false);

            return new CodingRemoteSessionThreadOperationResult
            {
                SessionId = sourceSession.Id,
                WorkspaceId = sourceWorkspace.Id,
                Action = CodingRemoteSessionThreadActions.Fork,
                Mode = request.Mode,
                TurnId = lastTurnId,
                CreatedSession = ToSummary(createdSession),
                SwitchToCreatedSession = request.SwitchToCreatedSession,
                StatusText = warning ?? Text("CodingRemote.ThreadOperation.ForkCreated", "Fork created.")
            };
        }

        private async Task<CodingRemoteSessionThreadOperationResult> CompactAsync(
            AppStateSnapshot snapshot,
            ChatSession session,
            CancellationToken cancellationToken)
        {
            var workspace = FindWorkspace(snapshot, session.WorkspaceId);
            if (workspace == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.WorkspaceNotFound, "Workspace was not found.");
            }

            var appServerClient = _threadClient as AppServerClient;
            if (appServerClient != null)
            {
                await appServerClient.SubmitCompactThreadRequestAsync(
                    session.ThreadId,
                    workspace.RootPath,
                    session.ApprovalMode,
                    session.ModelId).ConfigureAwait(false);
            }
            else
            {
                await _threadClient.CompactThreadAsync(
                    session.ThreadId,
                    workspace.RootPath,
                    session.ApprovalMode,
                    session.ModelId).ConfigureAwait(false);
            }

            session.LastActiveAt = DateTime.UtcNow;
            _appStateStore.Save(snapshot);

            return new CodingRemoteSessionThreadOperationResult
            {
                SessionId = session.Id,
                WorkspaceId = workspace.Id,
                Action = CodingRemoteSessionThreadActions.Compact,
                Mode = CodingRemoteSessionThreadOperationModes.Full,
                Accepted = true,
                StatusText = Text("CodingRemote.ThreadOperation.CompactionStarted", "Context compaction started.")
            };
        }

        private static string Text(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }

        private static ChatSession FindCodingSession(AppStateSnapshot snapshot, string sessionId)
        {
            var sessions = snapshot.Sessions ?? new List<ChatSession>();
            return sessions.FirstOrDefault(session =>
                session != null &&
                session.Mode == AppInteractionMode.Coding &&
                string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        }

        private static WorkspaceInfo FindWorkspace(AppStateSnapshot snapshot, string workspaceId)
        {
            var workspaces = snapshot.RecentWorkspaces ?? new List<WorkspaceInfo>();
            return workspaces.FirstOrDefault(workspace =>
                workspace != null &&
                workspace.Kind == WorkspaceKind.Coding &&
                string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase));
        }

        private static ChatSession CreateLocalSession(
            AppStateSnapshot snapshot,
            WorkspaceInfo targetWorkspace,
            ChatSession sourceSession,
            AppServerThreadSummary forkSummary,
            string title,
            bool isFork)
        {
            forkSummary = forkSummary ?? new AppServerThreadSummary();
            var now = DateTime.UtcNow;
            return new ChatSession
            {
                Id = Guid.NewGuid().ToString("N"),
                WorkspaceId = targetWorkspace.Id,
                ThreadId = forkSummary.ThreadId,
                Title = title,
                CreatedAt = forkSummary.CreatedAt == default(DateTime) ? now : forkSummary.CreatedAt,
                LastActiveAt = forkSummary.UpdatedAt == default(DateTime) ? now : forkSummary.UpdatedAt,
                Mode = AppInteractionMode.Coding,
                IsFork = isFork,
                ForkSourceThreadId = isFork ? sourceSession.ThreadId : null,
                ForkSourceTitle = isFork ? sourceSession.Title : null,
                RuntimeProfileId = sourceSession.RuntimeProfileId,
                ApprovalMode = sourceSession.ApprovalMode,
                ModelId = sourceSession.ModelId,
                ModelProviderId = forkSummary.ModelProviderId,
                ReasoningEffort = sourceSession.ReasoningEffort,
                IsPlanModeEnabled = sourceSession.IsPlanModeEnabled,
                Preview = forkSummary.Preview,
                SessionPath = forkSummary.SessionPath
            };
        }

        private void SaveCreatedSession(AppStateSnapshot snapshot, ChatSession createdSession, bool switchToCreatedSession)
        {
            if (snapshot.Sessions == null)
            {
                snapshot.Sessions = new List<ChatSession>();
            }

            snapshot.Sessions.Add(createdSession);
            if (switchToCreatedSession)
            {
                snapshot.CurrentSessionId = createdSession.Id;
                snapshot.CurrentCodingSessionId = createdSession.Id;
                snapshot.CurrentWorkspaceId = createdSession.WorkspaceId;
                snapshot.CurrentCodingWorkspaceId = createdSession.WorkspaceId;
            }

            _appStateStore.Save(snapshot);
        }

        private static CodingRemoteSessionSummary ToSummary(ChatSession session)
        {
            return new CodingRemoteSessionSummary
            {
                SessionId = session.Id,
                WorkspaceId = session.WorkspaceId,
                Title = session.Title,
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.LastActiveAt,
                BranchKind = CodingRemoteSessionBranchKinds.Fork,
                State = CodingRemoteSessionStates.Idle,
                LastMessagePreview = session.Preview
            };
        }

        private static string NormalizeTitle(string title, string sourceTitle, string suffix)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title.Trim();
            }

            var baseTitle = string.IsNullOrWhiteSpace(sourceTitle) ? "Session" : sourceTitle.Trim();
            return baseTitle + " " + suffix;
        }

    }
}
