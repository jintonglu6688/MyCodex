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

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class AppServerCodingRemoteAttachmentCatalogOperator : ICodingRemoteAttachmentCatalogOperator
    {
        private readonly IAppStateStore _appStateStore;
        private readonly IAppServerPluginClient _pluginClient;
        private readonly IAppServerSkillClient _skillClient;
        private readonly AppServerProtocolCapabilities _protocolCapabilities;
        private readonly AppInteractionMode _interactionMode;
        private readonly WorkspaceKind _workspaceKind;

        public AppServerCodingRemoteAttachmentCatalogOperator(IAppStateStore appStateStore, IAppServerClient appServerClient)
            : this(appStateStore, appServerClient, AppInteractionMode.Coding)
        {
        }

        internal AppServerCodingRemoteAttachmentCatalogOperator(
            IAppStateStore appStateStore,
            IAppServerPluginClient pluginClient,
            IAppServerSkillClient skillClient)
            : this(appStateStore, pluginClient, skillClient, null, AppInteractionMode.Coding)
        {
        }

        public AppServerCodingRemoteAttachmentCatalogOperator(
            IAppStateStore appStateStore,
            IAppServerClient appServerClient,
            AppInteractionMode interactionMode)
            : this(
                appStateStore,
                appServerClient,
                appServerClient,
                appServerClient == null ? null : appServerClient.ProtocolCapabilities,
                interactionMode)
        {
        }

        internal AppServerCodingRemoteAttachmentCatalogOperator(
            IAppStateStore appStateStore,
            IAppServerPluginClient pluginClient,
            IAppServerSkillClient skillClient,
            AppInteractionMode interactionMode)
            : this(appStateStore, pluginClient, skillClient, null, interactionMode)
        {
        }

        private AppServerCodingRemoteAttachmentCatalogOperator(
            IAppStateStore appStateStore,
            IAppServerPluginClient pluginClient,
            IAppServerSkillClient skillClient,
            AppServerProtocolCapabilities protocolCapabilities,
            AppInteractionMode interactionMode)
        {
            _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
            _pluginClient = pluginClient ?? throw new ArgumentNullException(nameof(pluginClient));
            _skillClient = skillClient ?? throw new ArgumentNullException(nameof(skillClient));
            _protocolCapabilities = protocolCapabilities;
            _interactionMode = interactionMode;
            _workspaceKind = interactionMode == AppInteractionMode.Office ? WorkspaceKind.Office : WorkspaceKind.Coding;
        }

        public async Task<CodingRemoteAttachmentCatalogResult> ListPluginAttachmentsAsync(string sessionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _appStateStore.Load() ?? new AppStateSnapshot();
            var session = FindCodingSession(snapshot, sessionId);
            if (session == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.SessionNotFound, "Session was not found.");
            }

            var installed = await AppServerInstalledPluginObservationService.LoadAsync(
                _pluginClient,
                _protocolCapabilities,
                cancellationToken).ConfigureAwait(false);
            var items = (installed.Plugins ?? new List<AppServerPluginSummary>())
                .Where(plugin => plugin != null && plugin.Enabled && !string.IsNullOrWhiteSpace(plugin.SourcePath))
                .Select(MapPlugin)
                .ToList();
            items = items
                .OrderByDescending(item => item.Enabled)
                .ThenBy(item => item.DisplayName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new CodingRemoteAttachmentCatalogResult
            {
                SessionId = session.Id,
                Items = items
            };
        }

        public async Task<CodingRemoteAttachmentCatalogResult> ListSkillAttachmentsAsync(string sessionId, bool forceReload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _appStateStore.Load() ?? new AppStateSnapshot();
            var session = FindCodingSession(snapshot, sessionId);
            if (session == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.SessionNotFound, "Session was not found.");
            }

            var workspace = FindCodingWorkspace(snapshot, session.WorkspaceId);
            if (workspace == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.WorkspaceNotFound, "Workspace was not found.");
            }

            var skillSnapshots = await _skillClient.ListSkillsAsync(new[] { workspace.RootPath }, forceReload).ConfigureAwait(false);
            var items = (skillSnapshots ?? new List<AppServerSkillsSnapshot>())
                .SelectMany(skillSnapshot => skillSnapshot == null ? new List<AppServerSkillInfo>() : skillSnapshot.Skills ?? new List<AppServerSkillInfo>())
                .Where(skill => skill != null && !string.IsNullOrWhiteSpace(skill.Path))
                .Select(MapSkill)
                .OrderByDescending(item => item.Enabled)
                .ThenBy(item => item.DisplayName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new CodingRemoteAttachmentCatalogResult
            {
                SessionId = session.Id,
                WorkspaceId = workspace.Id,
                Items = items
            };
        }

        private static CodingRemoteAttachmentCatalogItem MapPlugin(AppServerPluginSummary plugin)
        {
            return new CodingRemoteAttachmentCatalogItem
            {
                Kind = "plugin",
                Type = "mention",
                Name = plugin.Name,
                DisplayName = string.IsNullOrWhiteSpace(plugin.DisplayName) ? plugin.Name : plugin.DisplayName,
                Path = plugin.SourcePath,
                Enabled = plugin.Enabled,
                Installed = plugin.Installed,
                SourceText = plugin.MarketplaceName,
                Summary = string.IsNullOrWhiteSpace(plugin.ShortDescription) ? plugin.LongDescription : plugin.ShortDescription
            };
        }

        private static CodingRemoteAttachmentCatalogItem MapSkill(AppServerSkillInfo skill)
        {
            return new CodingRemoteAttachmentCatalogItem
            {
                Kind = "skill",
                Type = "skill",
                Name = skill.Name,
                DisplayName = string.IsNullOrWhiteSpace(skill.DisplayName) ? skill.Name : skill.DisplayName,
                Path = skill.Path,
                Enabled = skill.Enabled,
                Installed = true,
                SourceText = skill.Scope,
                Scope = skill.Scope,
                Summary = string.IsNullOrWhiteSpace(skill.ShortDescription) ? skill.Description : skill.ShortDescription
            };
        }

        private ChatSession FindCodingSession(AppStateSnapshot snapshot, string sessionId)
        {
            return (snapshot.Sessions ?? new List<ChatSession>())
                .FirstOrDefault(session =>
                    session != null &&
                    session.Mode == _interactionMode &&
                    string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        }

        private WorkspaceInfo FindCodingWorkspace(AppStateSnapshot snapshot, string workspaceId)
        {
            return (snapshot.RecentWorkspaces ?? new List<WorkspaceInfo>())
                .FirstOrDefault(workspace =>
                    workspace != null &&
                    workspace.Kind == _workspaceKind &&
                    string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(workspace.RootPath));
        }
    }
}
