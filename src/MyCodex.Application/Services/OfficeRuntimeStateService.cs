using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Services
{
    public sealed class OfficeRuntimeStateService
    {
        private readonly OfficeWorkspaceService _officeWorkspaceService;

        public OfficeRuntimeStateService()
            : this(new OfficeWorkspaceService())
        {
        }

        public OfficeRuntimeStateService(OfficeWorkspaceService officeWorkspaceService)
        {
            _officeWorkspaceService = officeWorkspaceService ?? throw new ArgumentNullException(nameof(officeWorkspaceService));
        }

        public OfficeRuntimeStateSnapshot GetOrCreateState(
            AppStateSnapshot snapshot,
            string runtimeScopeId)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var normalizedScopeId = NormalizeScopeId(runtimeScopeId);
            if (normalizedScopeId == null)
            {
                throw new ArgumentException("A runtime scope ID is required.", nameof(runtimeScopeId));
            }

            NormalizeStates(snapshot);
            foreach (var state in snapshot.OfficeRuntimeStates)
            {
                if (string.Equals(state.RuntimeScopeId, normalizedScopeId, StringComparison.OrdinalIgnoreCase))
                {
                    return state;
                }
            }

            var created = new OfficeRuntimeStateSnapshot { RuntimeScopeId = normalizedScopeId };
            snapshot.OfficeRuntimeStates.Add(created);
            return created;
        }

        public void NormalizeStates(AppStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var normalized = new List<OfficeRuntimeStateSnapshot>();
            var statesByScope = new Dictionary<string, OfficeRuntimeStateSnapshot>(StringComparer.OrdinalIgnoreCase);
            var source = snapshot.OfficeRuntimeStates ?? new List<OfficeRuntimeStateSnapshot>();
            foreach (var state in source)
            {
                if (state == null)
                {
                    continue;
                }

                var scopeId = NormalizeScopeId(state.RuntimeScopeId);
                if (scopeId == null)
                {
                    continue;
                }

                OfficeRuntimeStateSnapshot target;
                if (!statesByScope.TryGetValue(scopeId, out target))
                {
                    state.RuntimeScopeId = scopeId;
                    state.RecentWorkspaces = state.RecentWorkspaces ?? new List<WorkspaceInfo>();
                    target = state;
                    statesByScope.Add(scopeId, target);
                    normalized.Add(target);
                    continue;
                }

                MergeWorkspaces(target.RecentWorkspaces, state.RecentWorkspaces);
                if (string.IsNullOrWhiteSpace(target.CurrentWorkspaceId))
                {
                    target.CurrentWorkspaceId = state.CurrentWorkspaceId;
                }

                if (string.IsNullOrWhiteSpace(target.CurrentSessionId))
                {
                    target.CurrentSessionId = state.CurrentSessionId;
                }
            }

            snapshot.OfficeRuntimeStates = normalized;
        }

        public OfficeRuntimeStateSnapshot MigrateLegacyState(
            AppStateSnapshot snapshot,
            OfficeRuntimeTarget activeTarget)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (activeTarget == null)
            {
                throw new ArgumentNullException(nameof(activeTarget));
            }

            NormalizeStates(snapshot);
            var migrations = new Dictionary<string, WorkspaceMigration>(StringComparer.OrdinalIgnoreCase);
            if (snapshot.RecentWorkspaces != null)
            {
                foreach (var workspace in snapshot.RecentWorkspaces)
                {
                    MigrateLegacyWorkspace(snapshot, workspace, activeTarget, migrations);
                }
            }

            if (!string.IsNullOrWhiteSpace(snapshot.CurrentOfficeWorkspaceId))
            {
                WorkspaceMigration currentMigration;
                if (migrations.TryGetValue(snapshot.CurrentOfficeWorkspaceId, out currentMigration))
                {
                    snapshot.CurrentOfficeWorkspaceId = currentMigration.WorkspaceId;
                }
            }

            if (snapshot.Sessions != null)
            {
                foreach (var session in snapshot.Sessions)
                {
                    if (session == null || session.Mode != AppInteractionMode.Office)
                    {
                        continue;
                    }

                    WorkspaceMigration migration;
                    if (!string.IsNullOrWhiteSpace(session.WorkspaceId) &&
                        migrations.TryGetValue(session.WorkspaceId, out migration))
                    {
                        session.WorkspaceId = migration.WorkspaceId;
                        session.OfficeRuntimeScopeId = migration.RuntimeScopeId;
                    }
                    else if (string.IsNullOrWhiteSpace(session.OfficeRuntimeScopeId))
                    {
                        session.OfficeRuntimeScopeId = activeTarget.RuntimeScopeId;
                    }
                    else
                    {
                        session.OfficeRuntimeScopeId = NormalizeScopeId(session.OfficeRuntimeScopeId);
                    }

                    if (string.Equals(
                        session.Id,
                        snapshot.CurrentOfficeSessionId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        GetOrCreateState(snapshot, session.OfficeRuntimeScopeId).CurrentSessionId = session.Id;
                    }
                }
            }

            return GetOrCreateState(snapshot, activeTarget.RuntimeScopeId);
        }

        private void MigrateLegacyWorkspace(
            AppStateSnapshot snapshot,
            WorkspaceInfo workspace,
            OfficeRuntimeTarget activeTarget,
            IDictionary<string, WorkspaceMigration> migrations)
        {
            if (workspace == null ||
                workspace.Kind != WorkspaceKind.Office ||
                string.IsNullOrWhiteSpace(workspace.Id) ||
                string.IsNullOrWhiteSpace(workspace.RootPath) ||
                !IsLegacyOfficeWorkspaceId(workspace.Id))
            {
                return;
            }

            string runtimeScopeId;
            AppServerHostPlatform hostPlatform;
            if (WorkspacePathUtilities.LooksLikeWindowsDriveAbsolutePath(workspace.RootPath) ||
                WorkspacePathUtilities.LooksLikeWindowsUncAbsolutePath(workspace.RootPath))
            {
                runtimeScopeId = "windows";
                hostPlatform = AppServerHostPlatform.Windows;
            }
            else if (activeTarget.UsesUnixPaths &&
                     WorkspacePathUtilities.LooksLikeUnixAbsolutePath(workspace.RootPath))
            {
                runtimeScopeId = NormalizeScopeId(activeTarget.RuntimeScopeId);
                hostPlatform = activeTarget.HostPlatform;
            }
            else
            {
                return;
            }

            var oldWorkspaceId = workspace.Id;
            var newWorkspaceId = _officeWorkspaceService.CreateOfficeWorkspace(
                runtimeScopeId,
                hostPlatform,
                workspace.RootPath).Id;
            workspace.Id = newWorkspaceId;
            var state = GetOrCreateState(snapshot, runtimeScopeId);
            AddWorkspaceIfMissing(state.RecentWorkspaces, workspace);
            migrations[oldWorkspaceId] = new WorkspaceMigration(runtimeScopeId, newWorkspaceId);

            if (string.Equals(
                oldWorkspaceId,
                snapshot.CurrentOfficeWorkspaceId,
                StringComparison.OrdinalIgnoreCase))
            {
                state.CurrentWorkspaceId = newWorkspaceId;
            }
        }

        private static void MergeWorkspaces(ICollection<WorkspaceInfo> target, IEnumerable<WorkspaceInfo> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            foreach (var workspace in source)
            {
                AddWorkspaceIfMissing(target, workspace);
            }
        }

        private static void AddWorkspaceIfMissing(ICollection<WorkspaceInfo> target, WorkspaceInfo workspace)
        {
            if (target == null || workspace == null)
            {
                return;
            }

            foreach (var existing in target)
            {
                if (existing != null &&
                    string.Equals(existing.Id, workspace.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            target.Add(workspace);
        }

        private static bool IsLegacyOfficeWorkspaceId(string workspaceId)
        {
            if (string.IsNullOrWhiteSpace(workspaceId) ||
                !workspaceId.StartsWith("office:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var lastSeparator = workspaceId.LastIndexOf(':');
            var digest = lastSeparator < 0 ? string.Empty : workspaceId.Substring(lastSeparator + 1);
            if (digest.Length != 64)
            {
                return true;
            }

            for (var i = 0; i < digest.Length; i++)
            {
                var value = digest[i];
                if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f')))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeScopeId(string runtimeScopeId)
        {
            return string.IsNullOrWhiteSpace(runtimeScopeId)
                ? null
                : runtimeScopeId.Trim().ToLowerInvariant();
        }

        private sealed class WorkspaceMigration
        {
            public WorkspaceMigration(string runtimeScopeId, string workspaceId)
            {
                RuntimeScopeId = runtimeScopeId;
                WorkspaceId = workspaceId;
            }

            public string RuntimeScopeId { get; }

            public string WorkspaceId { get; }
        }
    }
}
