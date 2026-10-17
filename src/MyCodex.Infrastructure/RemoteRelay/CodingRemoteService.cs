using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class CodingRemoteService : ICodingRemoteService
    {
        private readonly IAppStateStore _appStateStore;
        private readonly ICodingRemoteThreadMessageReader _messageReader;
        private readonly ICodingRemoteMessageSender _messageSender;
        private readonly ICodingRemoteWorkspaceOperator _workspaceOperator;
        private readonly ICodingRemoteSessionOperator _sessionOperator;
        private readonly ICodingRemoteSessionSettingsOperator _sessionSettingsOperator;
        private readonly ICodingRemoteSessionCreator _sessionCreator;
        private readonly ICodingRemoteSessionThreadOperator _sessionThreadOperator;
        private readonly ICodingRemoteSessionStopper _sessionStopper;
        private readonly ICodingRemoteInteractionResponder _interactionResponder;
        private readonly ICodingRemoteAttachmentCatalogOperator _attachmentCatalogOperator;
        private readonly AppInteractionMode _interactionMode;
        private readonly WorkspaceKind _workspaceKind;
        private readonly string _remoteKind;
        private readonly IAppConfigStore _appConfigStore;
        private readonly OfficeWorkspaceService _officeWorkspaceService;
        private readonly OfficeRuntimeScopeService _officeRuntimeScopeService;
        private readonly IOfficeRuntimeFileSystem _officeRuntimeFileSystem;
        private readonly Func<AppConfigSnapshot, Task<ModeSettingsSnapshot>> _modeSettingsLoader;

        public CodingRemoteService(IAppStateStore appStateStore)
            : this(appStateStore, null, null, null, null, null, null)
        {
        }

        public CodingRemoteService(
            IAppStateStore appStateStore,
            ICodingRemoteThreadMessageReader messageReader,
            ICodingRemoteMessageSender messageSender,
            ICodingRemoteWorkspaceOperator workspaceOperator,
            ICodingRemoteSessionOperator sessionOperator,
            ICodingRemoteSessionSettingsOperator sessionSettingsOperator,
            ICodingRemoteSessionCreator sessionCreator,
            ICodingRemoteSessionThreadOperator sessionThreadOperator = null,
            ICodingRemoteSessionStopper sessionStopper = null,
            ICodingRemoteInteractionResponder interactionResponder = null,
            ICodingRemoteAttachmentCatalogOperator attachmentCatalogOperator = null)
            : this(
                appStateStore,
                messageReader,
                messageSender,
                workspaceOperator,
                sessionOperator,
                sessionSettingsOperator,
                sessionCreator,
                sessionThreadOperator,
                sessionStopper,
                interactionResponder,
                attachmentCatalogOperator,
                AppInteractionMode.Coding,
                null,
                null,
                null,
                null,
                null)
        {
        }

        public static CodingRemoteService CreateOffice(
            IAppStateStore appStateStore,
            IAppConfigStore appConfigStore,
            OfficeWorkspaceService officeWorkspaceService,
            ICodingRemoteThreadMessageReader messageReader = null,
            ICodingRemoteMessageSender messageSender = null,
            ICodingRemoteSessionOperator sessionOperator = null,
            ICodingRemoteSessionSettingsOperator sessionSettingsOperator = null,
            ICodingRemoteSessionCreator sessionCreator = null,
            ICodingRemoteSessionStopper sessionStopper = null,
            ICodingRemoteInteractionResponder interactionResponder = null,
            ICodingRemoteAttachmentCatalogOperator attachmentCatalogOperator = null,
            OfficeRuntimeScopeService officeRuntimeScopeService = null,
            IOfficeRuntimeFileSystem officeRuntimeFileSystem = null,
            Func<AppConfigSnapshot, Task<ModeSettingsSnapshot>> modeSettingsLoader = null)
        {
            return new CodingRemoteService(
                appStateStore,
                messageReader,
                messageSender,
                null,
                sessionOperator,
                sessionSettingsOperator,
                sessionCreator,
                null,
                sessionStopper,
                interactionResponder,
                attachmentCatalogOperator,
                AppInteractionMode.Office,
                appConfigStore,
                officeWorkspaceService,
                officeRuntimeScopeService ?? new OfficeRuntimeScopeService(),
                officeRuntimeFileSystem ?? new OfficeRuntimeFileSystem(),
                modeSettingsLoader);
        }

        private CodingRemoteService(
            IAppStateStore appStateStore,
            ICodingRemoteThreadMessageReader messageReader,
            ICodingRemoteMessageSender messageSender,
            ICodingRemoteWorkspaceOperator workspaceOperator,
            ICodingRemoteSessionOperator sessionOperator,
            ICodingRemoteSessionSettingsOperator sessionSettingsOperator,
            ICodingRemoteSessionCreator sessionCreator,
            ICodingRemoteSessionThreadOperator sessionThreadOperator,
            ICodingRemoteSessionStopper sessionStopper,
            ICodingRemoteInteractionResponder interactionResponder,
            ICodingRemoteAttachmentCatalogOperator attachmentCatalogOperator,
            AppInteractionMode interactionMode,
            IAppConfigStore appConfigStore,
            OfficeWorkspaceService officeWorkspaceService,
            OfficeRuntimeScopeService officeRuntimeScopeService,
            IOfficeRuntimeFileSystem officeRuntimeFileSystem,
            Func<AppConfigSnapshot, Task<ModeSettingsSnapshot>> modeSettingsLoader)
        {
            _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
            _messageReader = messageReader;
            _messageSender = messageSender;
            _workspaceOperator = workspaceOperator;
            _sessionOperator = sessionOperator;
            _sessionSettingsOperator = sessionSettingsOperator;
            _sessionCreator = sessionCreator;
            _sessionThreadOperator = sessionThreadOperator;
            _sessionStopper = sessionStopper;
            _interactionResponder = interactionResponder;
            _attachmentCatalogOperator = attachmentCatalogOperator;
            _interactionMode = interactionMode;
            _workspaceKind = interactionMode == AppInteractionMode.Office ? WorkspaceKind.Office : WorkspaceKind.Coding;
            _remoteKind = interactionMode == AppInteractionMode.Office ? CodingRemoteKinds.Office : CodingRemoteKinds.Coding;
            _appConfigStore = appConfigStore;
            _officeWorkspaceService = officeWorkspaceService;
            _officeRuntimeScopeService = officeRuntimeScopeService;
            _officeRuntimeFileSystem = officeRuntimeFileSystem;
            _modeSettingsLoader = modeSettingsLoader;
        }

        public async Task<IReadOnlyList<CodingRemoteWorkspaceSummary>> ListWorkspacesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = LoadSnapshot();
            var officeResult = await EnsureDefaultOfficeWorkspaceAsync(snapshot, cancellationToken);
            var workspaces = (snapshot.RecentWorkspaces ?? new List<WorkspaceInfo>())
                .Where(workspace => workspace != null &&
                    workspace.Kind == _workspaceKind &&
                    (_interactionMode != AppInteractionMode.Office ||
                     IsOfficeWorkspaceForTarget(workspace, officeResult.Context.Target)))
                .Select(MapWorkspace)
                .ToList();
            if (officeResult.Saved)
            {
                _appStateStore.Save(snapshot);
            }

            return workspaces;
        }

        public Task<IReadOnlyList<CodingRemoteSessionSummary>> ListSessionsAsync(string workspaceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = LoadSnapshot();
            var workspaceSessions = (snapshot.Sessions ?? new List<ChatSession>())
                .Where(session => IsSessionForWorkspace(session, workspaceId))
                .OrderByDescending(session => ResolveUpdatedAt(session))
                .ToList();
            var sessions = BuildMainSessionEntries(workspaceSessions)
                .Select(entry => MapSession(entry.Session, entry.SubAgentCount, entry.SubAgents))
                .ToList();

            return Task.FromResult((IReadOnlyList<CodingRemoteSessionSummary>)sessions);
        }

        public async Task<CodingRemoteSessionSummary> CreateSessionAsync(CodingRemoteSessionCreateRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_sessionCreator == null)
            {
                throw new InvalidOperationException("Remote session creation is not available.");
            }

            return await _sessionCreator.CreateSessionAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public async Task<CodingRemoteMessagePage> ListMessagesAsync(CodingRemoteMessagesListRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new CodingRemoteMessagesListRequest();
            if (_messageReader != null)
            {
                var page = await _messageReader.ListMessagesAsync(request, cancellationToken).ConfigureAwait(false);
                if (page != null)
                {
                    return page;
                }
            }

            return new CodingRemoteMessagePage
            {
                SessionId = request.SessionId
            };
        }

        public async Task<CodingRemoteMessageContentPage> ReadMessageContentAsync(CodingRemoteMessageContentRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new CodingRemoteMessageContentRequest();
            if (_messageReader == null)
            {
                throw new InvalidOperationException("Remote message content is not available.");
            }

            return await _messageReader.ReadMessageContentAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public async Task<CodingRemoteMessageSendResult> SendMessageAsync(CodingRemoteMessageSendRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_messageSender == null)
            {
                throw new InvalidOperationException("Remote message sending is not available.");
            }

            return await _messageSender.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public async Task<CodingRemoteAttachmentCatalogResult> ListPluginAttachmentsAsync(string sessionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_attachmentCatalogOperator == null)
            {
                throw new InvalidOperationException("Remote attachment catalog is not available.");
            }

            return await _attachmentCatalogOperator.ListPluginAttachmentsAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<CodingRemoteAttachmentCatalogResult> ListSkillAttachmentsAsync(string sessionId, bool forceReload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_attachmentCatalogOperator == null)
            {
                throw new InvalidOperationException("Remote attachment catalog is not available.");
            }

            return await _attachmentCatalogOperator.ListSkillAttachmentsAsync(sessionId, forceReload, cancellationToken).ConfigureAwait(false);
        }

        public Task<CodingRemoteSessionStopResult> StopSessionAsync(CodingRemoteSessionStopRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_sessionStopper == null)
            {
                throw new InvalidOperationException("Remote session stop is not available.");
            }

            return _sessionStopper.StopSessionAsync(request, cancellationToken);
        }

        public async Task<CodingRemoteWorkspaceOperationResult> OperateWorkspaceAsync(CodingRemoteWorkspaceOperationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_workspaceOperator == null)
            {
                throw new InvalidOperationException("Remote workspace operations are not available.");
            }

            return await _workspaceOperator.OperateWorkspaceAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public async Task<CodingRemoteSessionOperationResult> OperateSessionAsync(CodingRemoteSessionOperationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_sessionOperator == null)
            {
                throw new InvalidOperationException("Remote session operations are not available.");
            }

            return await _sessionOperator.OperateSessionAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public Task<CodingRemoteSessionThreadOperationResult> OperateSessionThreadAsync(
            CodingRemoteSessionThreadOperationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_sessionThreadOperator == null)
            {
                throw new InvalidOperationException("Remote session thread operations are not available.");
            }

            return _sessionThreadOperator.OperateSessionThreadAsync(request, cancellationToken);
        }

        public async Task<CodingRemoteSessionSettings> GetSessionSettingsAsync(string sessionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_sessionSettingsOperator == null)
            {
                throw new InvalidOperationException("Remote session settings are not available.");
            }

            return await _sessionSettingsOperator.GetSessionSettingsAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<CodingRemoteSessionSettingsUpdateResult> UpdateSessionSettingsAsync(CodingRemoteSessionSettingsUpdateRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_sessionSettingsOperator == null)
            {
                throw new InvalidOperationException("Remote session settings are not available.");
            }

            return await _sessionSettingsOperator.UpdateSessionSettingsAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public Task<CodingRemoteApprovalResponseResult> RespondApprovalAsync(
            CodingRemoteApprovalResponseRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_interactionResponder == null)
            {
                throw new InvalidOperationException("Remote approval response is not available.");
            }

            return _interactionResponder.RespondApprovalAsync(request, cancellationToken);
        }

        public Task<CodingRemoteToolInputResponseResult> RespondToolInputAsync(
            CodingRemoteToolInputRespondRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_interactionResponder == null)
            {
                throw new InvalidOperationException("Remote tool input response is not available.");
            }

            return _interactionResponder.RespondToolInputAsync(request, cancellationToken);
        }

        public Task<CodingRemoteToolInputResponseResult> CancelToolInputAsync(
            CodingRemoteToolInputCancelRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_interactionResponder == null)
            {
                throw new InvalidOperationException("Remote tool input cancellation is not available.");
            }

            return _interactionResponder.CancelToolInputAsync(request, cancellationToken);
        }

        private AppStateSnapshot LoadSnapshot()
        {
            return _appStateStore.Load() ?? new AppStateSnapshot();
        }

        private CodingRemoteWorkspaceSummary MapWorkspace(WorkspaceInfo workspace)
        {
            return new CodingRemoteWorkspaceSummary
            {
                WorkspaceId = workspace.Id,
                DisplayName = string.IsNullOrWhiteSpace(workspace.Name) ? workspace.RootPath : workspace.Name,
                Path = workspace.RootPath,
                LastUsedAt = workspace.LastOpenedAt,
                Kind = _remoteKind
            };
        }

        private static CodingRemoteSessionSummary MapSession(ChatSession session, int subAgentCount, IReadOnlyList<ChatSession> subAgents)
        {
            return new CodingRemoteSessionSummary
            {
                SessionId = session.Id,
                WorkspaceId = session.WorkspaceId,
                Title = BuildNormalizedTitle(session.Title),
                CreatedAt = session.CreatedAt,
                UpdatedAt = ResolveUpdatedAt(session),
                BranchKind = ResolveBranchKind(session),
                SubAgentCount = subAgentCount,
                SubAgents = (subAgents ?? Array.Empty<ChatSession>())
                    .Where(subAgent => subAgent != null)
                    .Select(subAgent => MapSession(subAgent, 0, Array.Empty<ChatSession>()))
                    .ToList(),
                SubAgentName = session.SubAgentNickname,
                SubAgentRole = session.SubAgentRole,
                LastMessagePreview = session.Preview,
                State = CodingRemoteSessionStates.Idle
            };
        }

        private static IReadOnlyList<CodingRemoteSessionEntry> BuildMainSessionEntries(IReadOnlyList<ChatSession> sessions)
        {
            var subAgentsByParentThreadId = new Dictionary<string, List<ChatSession>>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in sessions)
            {
                if (!IsSubAgent(session) || string.IsNullOrWhiteSpace(session.ParentThreadId))
                {
                    continue;
                }

                List<ChatSession> subAgents;
                if (!subAgentsByParentThreadId.TryGetValue(session.ParentThreadId, out subAgents))
                {
                    subAgents = new List<ChatSession>();
                    subAgentsByParentThreadId[session.ParentThreadId] = subAgents;
                }

                subAgents.Add(session);
            }

            var entries = new List<CodingRemoteSessionEntry>();
            foreach (var session in sessions)
            {
                if (ShouldHideSubAgentInMainList(session, sessions))
                {
                    continue;
                }

                List<ChatSession> subAgents;
                IReadOnlyList<ChatSession> mappedSubAgents = Array.Empty<ChatSession>();
                if (!string.IsNullOrWhiteSpace(session.ThreadId) &&
                    subAgentsByParentThreadId.TryGetValue(session.ThreadId, out subAgents))
                {
                    mappedSubAgents = subAgents;
                }

                entries.Add(new CodingRemoteSessionEntry(session, mappedSubAgents.Count, mappedSubAgents));
            }

            return entries;
        }

        private bool IsSessionForWorkspace(ChatSession session, string workspaceId)
        {
            return session != null &&
                   session.Mode == _interactionMode &&
                   !string.IsNullOrWhiteSpace(session.WorkspaceId) &&
                   string.Equals(session.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<OfficeWorkspaceEnsureResult> EnsureDefaultOfficeWorkspaceAsync(
            AppStateSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (_interactionMode != AppInteractionMode.Office)
            {
                return new OfficeWorkspaceEnsureResult(null, false);
            }

            EnsureLists(snapshot);
            var appConfig = LoadAppConfig();
            var officeWorkspaceService = ResolveOfficeWorkspaceService();
            var context = await ResolveOfficeRuntimeContextAsync(appConfig, cancellationToken);
            await EnsureOfficeDirectoryAsync(appConfig, context.RootPath);
            var defaultWorkspace = officeWorkspaceService.CreateOfficeWorkspace(
                context.Target.RuntimeScopeId,
                context.Target.HostPlatform,
                context.RootPath);

            var changed = NormalizeOfficeWorkspacesForTarget(
                snapshot,
                context,
                officeWorkspaceService);

            var matches = snapshot.RecentWorkspaces.Where(workspace =>
                workspace != null &&
                workspace.Kind == WorkspaceKind.Office &&
                string.Equals(workspace.Id, defaultWorkspace.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var existing = matches.FirstOrDefault();
            if (existing != null)
            {
                existing.Kind = WorkspaceKind.Office;
                if (!string.Equals(existing.Name, defaultWorkspace.Name, StringComparison.Ordinal) ||
                    !PathsEqual(existing.RootPath, defaultWorkspace.RootPath))
                {
                    existing.Name = defaultWorkspace.Name;
                    existing.RootPath = defaultWorkspace.RootPath;
                    changed = true;
                }

                foreach (var duplicate in matches.Skip(1).ToList())
                {
                    snapshot.RecentWorkspaces.Remove(duplicate);
                    changed = true;
                }

                return new OfficeWorkspaceEnsureResult(context, changed);
            }

            snapshot.RecentWorkspaces.Insert(0, defaultWorkspace);
            return new OfficeWorkspaceEnsureResult(context, true);
        }

        private async Task<OfficeRuntimeContext> ResolveOfficeRuntimeContextAsync(
            AppConfigSnapshot appConfig,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = _officeRuntimeScopeService.ResolveTarget(appConfig);
            var modeSettings = _modeSettingsLoader == null
                ? new ModeSettingsSnapshot()
                : await _modeSettingsLoader(appConfig).ConfigureAwait(false);
            var configuredRootPath = modeSettings == null ? null : modeSettings.OfficeRootPath;
            string homePath = null;
            if (target.UsesUnixPaths && RequiresUnixHomeResolution(configuredRootPath))
            {
                homePath = await _officeRuntimeFileSystem.ResolveHomeDirectoryAsync(appConfig);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var rootPath = ResolveOfficeWorkspaceService().ResolveOfficeRootPath(
                target.HostPlatform,
                configuredRootPath,
                homePath);
            return new OfficeRuntimeContext(target, rootPath);
        }

        private async Task EnsureOfficeDirectoryAsync(AppConfigSnapshot appConfig, string path)
        {
            var result = await _officeRuntimeFileSystem.EnsureDirectoryAsync(appConfig, path);
            if (result == null || !result.Success)
            {
                throw new CodingRemoteOperationException(
                    RemoteErrorCodes.OperationFailed,
                    result == null || string.IsNullOrWhiteSpace(result.Message)
                        ? "Office directory is unavailable."
                        : result.Message);
            }
        }

        private static bool RequiresUnixHomeResolution(string configuredRootPath)
        {
            if (string.IsNullOrWhiteSpace(configuredRootPath))
            {
                return true;
            }

            var value = configuredRootPath.Trim().Trim('"');
            return string.Equals(value, "~", StringComparison.Ordinal) ||
                   value.StartsWith("~/", StringComparison.Ordinal) ||
                   string.Equals(value, "$HOME", StringComparison.Ordinal) ||
                   value.StartsWith("$HOME/", StringComparison.Ordinal);
        }

        private static bool NormalizeOfficeWorkspacesForTarget(
            AppStateSnapshot snapshot,
            OfficeRuntimeContext context,
            OfficeWorkspaceService officeWorkspaceService)
        {
            var changed = false;
            foreach (var workspace in snapshot.RecentWorkspaces ?? Enumerable.Empty<WorkspaceInfo>())
            {
                if (workspace == null ||
                    workspace.Kind != WorkspaceKind.Office ||
                    string.IsNullOrWhiteSpace(workspace.RootPath))
                {
                    continue;
                }

                WorkspaceInfo normalized;
                try
                {
                    normalized = officeWorkspaceService.ResolveOfficeWorkspaceForImport(
                        context.Target.RuntimeScopeId,
                        context.Target.HostPlatform,
                        context.RootPath,
                        workspace.RootPath,
                        snapshot.RecentWorkspaces);
                }
                catch
                {
                    continue;
                }

                if (!string.Equals(workspace.Id, normalized.Id, StringComparison.OrdinalIgnoreCase))
                {
                    var previousWorkspaceId = workspace.Id;
                    workspace.Id = normalized.Id;
                    UpdateOfficeWorkspaceReferences(snapshot, previousWorkspaceId, normalized.Id);
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(workspace.Name))
                {
                    workspace.Name = normalized.Name;
                    changed = true;
                }
            }

            return changed;
        }

        private static void UpdateOfficeWorkspaceReferences(
            AppStateSnapshot snapshot,
            string previousWorkspaceId,
            string currentWorkspaceId)
        {
            if (snapshot == null ||
                string.IsNullOrWhiteSpace(previousWorkspaceId) ||
                string.IsNullOrWhiteSpace(currentWorkspaceId))
            {
                return;
            }

            foreach (var session in snapshot.Sessions ?? Enumerable.Empty<ChatSession>())
            {
                if (session != null &&
                    session.Mode == AppInteractionMode.Office &&
                    string.Equals(session.WorkspaceId, previousWorkspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    session.WorkspaceId = currentWorkspaceId;
                }
            }

            if (string.Equals(snapshot.CurrentOfficeWorkspaceId, previousWorkspaceId, StringComparison.OrdinalIgnoreCase))
            {
                snapshot.CurrentOfficeWorkspaceId = currentWorkspaceId;
            }

            if (snapshot.CurrentInteractionMode == AppInteractionMode.Office &&
                string.Equals(snapshot.CurrentWorkspaceId, previousWorkspaceId, StringComparison.OrdinalIgnoreCase))
            {
                snapshot.CurrentWorkspaceId = currentWorkspaceId;
            }
        }

        private static bool IsOfficeWorkspaceForTarget(
            WorkspaceInfo workspace,
            OfficeRuntimeTarget target)
        {
            return workspace != null &&
                target != null &&
                !string.IsNullOrWhiteSpace(workspace.Id) &&
                string.Equals(
                    workspace.Id,
                    "office:" + target.RuntimeScopeId,
                    StringComparison.OrdinalIgnoreCase);
        }

        private sealed class OfficeRuntimeContext
        {
            public OfficeRuntimeContext(OfficeRuntimeTarget target, string rootPath)
            {
                Target = target;
                RootPath = rootPath;
            }

            public OfficeRuntimeTarget Target { get; }

            public string RootPath { get; }
        }

        private sealed class OfficeWorkspaceEnsureResult
        {
            public OfficeWorkspaceEnsureResult(OfficeRuntimeContext context, bool saved)
            {
                Context = context;
                Saved = saved;
            }

            public OfficeRuntimeContext Context { get; }

            public bool Saved { get; }
        }

        private AppConfigSnapshot LoadAppConfig()
        {
            return _appConfigStore == null ? new AppConfigSnapshot() : _appConfigStore.Load() ?? new AppConfigSnapshot();
        }

        private OfficeWorkspaceService ResolveOfficeWorkspaceService()
        {
            return _officeWorkspaceService ?? new OfficeWorkspaceService();
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

        private static bool PathsEqual(string firstPath, string secondPath)
        {
            if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSubAgent(ChatSession session)
        {
            return session != null &&
                   (session.IsSubAgent ||
                    string.Equals(session.ThreadSource, "subagent", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(session.ParentThreadId));
        }

        private static bool ShouldHideSubAgentInMainList(ChatSession session, IEnumerable<ChatSession> sessions)
        {
            if (!IsSubAgent(session))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(session.ParentThreadId) ||
                   HasParentInList(session, sessions);
        }

        private static bool HasParentInList(ChatSession session, IEnumerable<ChatSession> sessions)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ParentThreadId) || sessions == null)
            {
                return false;
            }

            return sessions.Any(candidate =>
                candidate != null &&
                !string.IsNullOrWhiteSpace(candidate.ThreadId) &&
                string.Equals(candidate.ThreadId, session.ParentThreadId, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveBranchKind(ChatSession session)
        {
            if (session == null)
            {
                return CodingRemoteSessionBranchKinds.Mainline;
            }

            if (session.IsReview)
            {
                return CodingRemoteSessionBranchKinds.Review;
            }

            return session.IsFork
                ? CodingRemoteSessionBranchKinds.Fork
                : CodingRemoteSessionBranchKinds.Mainline;
        }

        private static string BuildNormalizedTitle(string title)
        {
            var candidate = string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return "Untitled session";
            }

            candidate = TrimTypedTitlePrefix(candidate);
            while (TryTrimForkSuffix(candidate, out var trimmed))
            {
                candidate = trimmed;
            }

            candidate = TrimTypedTitlePrefix(candidate);
            return string.IsNullOrWhiteSpace(candidate) ? "Untitled session" : candidate;
        }

        private static string TrimTypedTitlePrefix(string title)
        {
            var match = Regex.Match(title ?? string.Empty, @"^(?<label>[^:：|]+)\s*(?:[:：]|\|)\s*(?<base>.+)$", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return title;
            }

            var label = match.Groups["label"].Value.Trim();
            return IsKnownBranchLabel(label)
                ? match.Groups["base"].Value.Trim()
                : title;
        }

        private static bool TryTrimForkSuffix(string title, out string trimmed)
        {
            var match = Regex.Match(title ?? string.Empty, @"^(?<base>.+?)\s-\s(?<label>.+?)\s(?<index>\d+)$", RegexOptions.CultureInvariant);
            if (!match.Success || !IsKnownForkLabel(match.Groups["label"].Value.Trim()))
            {
                trimmed = title;
                return false;
            }

            trimmed = match.Groups["base"].Value.Trim();
            return true;
        }

        private static bool IsKnownBranchLabel(string label)
        {
            return string.Equals(label, "Mainline", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(label, "Fork", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(label, "Review", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(label, "主线", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(label, "分叉", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(label, "审核", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(label, "评审", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKnownForkLabel(string label)
        {
            return string.Equals(label, "Fork", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(label, "分叉", StringComparison.OrdinalIgnoreCase);
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

        private sealed class CodingRemoteSessionEntry
        {
            public CodingRemoteSessionEntry(ChatSession session, int subAgentCount, IReadOnlyList<ChatSession> subAgents)
            {
                Session = session;
                SubAgentCount = subAgentCount;
                SubAgents = subAgents ?? Array.Empty<ChatSession>();
            }

            public ChatSession Session { get; }

            public int SubAgentCount { get; }

            public IReadOnlyList<ChatSession> SubAgents { get; }
        }
    }
}
