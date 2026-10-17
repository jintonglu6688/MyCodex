using System;
using System.Collections.Generic;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.Persistence
{
    internal static class MigrationDateNormalization
    {
        public static void Apply(AppStateSnapshot state, AutomationStateSnapshot jobs)
        {
            foreach (var session in state.Sessions ?? new List<ChatSession>())
            {
                if (session == null) continue;
                session.CreatedAt = Normalize(session.CreatedAt);
                session.LastActiveAt = Normalize(session.LastActiveAt);
            }
            NormalizeWorkspaces(state.RecentWorkspaces);
            foreach (var profile in state.RuntimeProfiles ?? new List<RuntimeProfileStateSnapshot>()) NormalizeWorkspaces(profile?.RecentWorkspaces);
            foreach (var profile in state.OfficeRuntimeStates ?? new List<OfficeRuntimeStateSnapshot>()) NormalizeWorkspaces(profile?.RecentWorkspaces);
            foreach (var definition in jobs.Definitions ?? new List<AutomationDefinitionSnapshot>())
            {
                if (definition == null) continue;
                definition.CreatedAt = Normalize(definition.CreatedAt);
                definition.UpdatedAt = Normalize(definition.UpdatedAt);
                definition.LastRunAt = NormalizeNullable(definition.LastRunAt);
                definition.NextRunAt = NormalizeNullable(definition.NextRunAt);
            }
            foreach (var run in jobs.Runs ?? new List<AutomationRunSnapshot>())
            {
                if (run == null) continue;
                run.ScheduledFor = NormalizeNullable(run.ScheduledFor);
                run.StartedAt = NormalizeNullable(run.StartedAt);
                run.CompletedAt = NormalizeNullable(run.CompletedAt);
            }
        }

        private static void NormalizeWorkspaces(IEnumerable<WorkspaceInfo> workspaces)
        {
            foreach (var workspace in workspaces ?? new WorkspaceInfo[0])
                if (workspace != null) workspace.LastOpenedAt = Normalize(workspace.LastOpenedAt);
        }

        private static DateTime? NormalizeNullable(DateTime? date) { return date.HasValue ? Normalize(date.Value) : (DateTime?)null; }

        private static DateTime Normalize(DateTime date)
        {
            return date == DateTime.MinValue ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) : date;
        }
    }
}
