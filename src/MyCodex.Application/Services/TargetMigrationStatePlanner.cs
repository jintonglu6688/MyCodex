using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Services
{
    public static class TargetMigrationStatePlanner
    {
        // Input is a private deserialized copy. Unknown records are retained in the migration's pending files.
        public static void SelectState(AppStateSnapshot state, string scope, ISet<string> knownScopes, IEnumerable<string> aliases = null)
        {
            var selectedScopes = new HashSet<string>(aliases ?? new string[0], StringComparer.OrdinalIgnoreCase);
            if (scope != null) selectedScopes.Add(scope);
            var assignedWorkspaceIds = new HashSet<string>((state.Sessions ?? new List<ChatSession>())
                .Where(s => s != null && SessionScope(s) != null && knownScopes.Contains(SessionScope(s))).Select(s => s.WorkspaceId));
            foreach (var profile in state.RuntimeProfiles ?? new List<RuntimeProfileStateSnapshot>())
            {
                if (profile != null && profile.RuntimeProfileId != null && knownScopes.Contains(profile.RuntimeProfileId))
                    foreach (var workspace in profile.RecentWorkspaces ?? new List<WorkspaceInfo>()) if (workspace != null) assignedWorkspaceIds.Add(workspace.Id);
            }
            foreach (var profile in state.OfficeRuntimeStates ?? new List<OfficeRuntimeStateSnapshot>())
            {
                if (profile != null && profile.RuntimeScopeId != null && knownScopes.Contains(profile.RuntimeScopeId))
                    foreach (var workspace in profile.RecentWorkspaces ?? new List<WorkspaceInfo>()) if (workspace != null) assignedWorkspaceIds.Add(workspace.Id);
            }
            state.Sessions = (state.Sessions ?? new List<ChatSession>()).Where(s =>
                s != null && Matches(SessionScope(s), scope, knownScopes, selectedScopes)).ToList();
            state.RuntimeProfiles = (state.RuntimeProfiles ?? new List<RuntimeProfileStateSnapshot>()).Where(p =>
                p != null && Matches(p.RuntimeProfileId, scope, knownScopes, selectedScopes)).ToList();
            state.OfficeRuntimeStates = (state.OfficeRuntimeStates ?? new List<OfficeRuntimeStateSnapshot>()).Where(p =>
                p != null && Matches(p.RuntimeScopeId, scope, knownScopes, selectedScopes)).ToList();
            if (scope != null)
            {
                foreach (var session in state.Sessions)
                {
                    if (session.Mode == AppInteractionMode.Office) session.OfficeRuntimeScopeId = scope;
                    else session.RuntimeProfileId = scope;
                }
                foreach (var profile in state.RuntimeProfiles) profile.RuntimeProfileId = scope;
                foreach (var profile in state.OfficeRuntimeStates) profile.RuntimeScopeId = scope;
            }
            var workspaceIds = new HashSet<string>(state.Sessions.Select(s => s.WorkspaceId));
            foreach (var profile in state.RuntimeProfiles)
            {
                foreach (var workspace in profile.RecentWorkspaces ?? new List<WorkspaceInfo>())
                {
                    if (workspace != null) workspaceIds.Add(workspace.Id);
                }
            }
            foreach (var profile in state.OfficeRuntimeStates)
            {
                foreach (var workspace in profile.RecentWorkspaces ?? new List<WorkspaceInfo>())
                {
                    if (workspace != null) workspaceIds.Add(workspace.Id);
                }
            }
            state.RecentWorkspaces = (state.RecentWorkspaces ?? new List<WorkspaceInfo>()).Where(w => w != null &&
                (workspaceIds.Contains(w.Id) || scope == "windows" && IsWindowsPath(w.RootPath) && !assignedWorkspaceIds.Contains(w.Id) ||
                 scope == null && !IsWindowsPath(w.RootPath) && !assignedWorkspaceIds.Contains(w.Id))).ToList();
            var sessions = new HashSet<string>(state.Sessions.Select(s => s.Id));
            var workspaces = new HashSet<string>(state.RecentWorkspaces.Select(w => w.Id).Concat(workspaceIds));
            if (!sessions.Contains(state.CurrentSessionId)) state.CurrentSessionId = null;
            if (!sessions.Contains(state.CurrentCodingSessionId)) state.CurrentCodingSessionId = null;
            if (!sessions.Contains(state.CurrentOfficeSessionId)) state.CurrentOfficeSessionId = null;
            if (!workspaces.Contains(state.CurrentWorkspaceId)) state.CurrentWorkspaceId = null;
            if (!workspaces.Contains(state.CurrentCodingWorkspaceId)) state.CurrentCodingWorkspaceId = null;
            if (!workspaces.Contains(state.CurrentOfficeWorkspaceId)) state.CurrentOfficeWorkspaceId = null;
        }

        public static void SelectAutomations(AutomationStateSnapshot state, string scope, ISet<string> knownScopes, IEnumerable<string> aliases = null)
        {
            var selectedScopes = new HashSet<string>(aliases ?? new string[0], StringComparer.OrdinalIgnoreCase);
            if (scope != null) selectedScopes.Add(scope);
            var allIds = new HashSet<string>((state.Definitions ?? new List<AutomationDefinitionSnapshot>()).Where(d => d != null).Select(d => d.Id));
            state.Definitions = (state.Definitions ?? new List<AutomationDefinitionSnapshot>()).Where(d =>
                d != null && Matches(AutomationScope(d), scope, knownScopes, selectedScopes)).ToList();
            var ids = new HashSet<string>(state.Definitions.Select(d => d.Id));
            state.Runs = (state.Runs ?? new List<AutomationRunSnapshot>()).Where(r =>
                r != null && (ids.Contains(r.AutomationId) || scope == null && !allIds.Contains(r.AutomationId))).ToList();
            if (scope == null)
            {
                foreach (var definition in state.Definitions)
                {
                    definition.Enabled = false;
                    definition.NextRunAt = null;
                }
            }
            else
            {
                foreach (var definition in state.Definitions)
                {
                    if (definition.Context.InteractionMode == AppInteractionMode.Office) definition.Context.OfficeRuntimeScopeId = scope;
                    else definition.Context.RuntimeProfileId = scope;
                }
            }
        }

        private static string SessionScope(ChatSession session)
        {
            var scope = session.Mode == AppInteractionMode.Office ? session.OfficeRuntimeScopeId : session.RuntimeProfileId;
            if (!string.IsNullOrWhiteSpace(scope)) return scope;
            if (session.Mode == AppInteractionMode.Office)
                return !string.IsNullOrWhiteSpace(session.RuntimeProfileId) ? session.RuntimeProfileId : IsWindowsPath(session.SessionPath) ? "windows" : null;
            // The legacy local state store treated unscoped Windows sessions as Windows.
            return (session.SessionPath ?? string.Empty).StartsWith("/", StringComparison.Ordinal) ||
                (session.WorkspaceId ?? string.Empty).StartsWith("/", StringComparison.Ordinal) ? null : "windows";
        }

        private static string AutomationScope(AutomationDefinitionSnapshot definition)
        {
            var context = definition.Context;
            return context == null ? null : context.InteractionMode == AppInteractionMode.Office
                ? context.OfficeRuntimeScopeId : context.RuntimeProfileId;
        }

        private static bool Matches(string candidate, string scope, ISet<string> knownScopes, ISet<string> selectedScopes)
        {
            return scope == null ? string.IsNullOrWhiteSpace(candidate) || !knownScopes.Contains(candidate)
                : candidate != null && selectedScopes.Contains(candidate);
        }

        private static bool IsWindowsPath(string path)
        {
            return !string.IsNullOrEmpty(path) && (path.Length > 2 && path[1] == ':' || path.StartsWith("\\\\", StringComparison.Ordinal));
        }
    }
}
