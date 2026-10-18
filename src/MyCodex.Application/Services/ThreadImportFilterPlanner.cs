using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Services
{
    public static class ThreadImportFilterPlanner
    {
        public static bool BelongsToKnownWorkspace(
            string workspacePath,
            string workspaceId,
            IEnumerable<WorkspaceInfo> recentWorkspaces,
            AppInteractionMode interactionMode,
            string officeRootPath,
            Func<string, string, bool> isSameOrChildPath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath) ||
                string.IsNullOrWhiteSpace(workspaceId) ||
                recentWorkspaces == null)
            {
                return false;
            }

            foreach (var workspace in recentWorkspaces)
            {
                if (workspace == null)
                {
                    continue;
                }

                if (interactionMode == AppInteractionMode.Office &&
                    isSameOrChildPath != null &&
                    isSameOrChildPath(workspacePath, officeRootPath))
                {
                    return true;
                }

                if (workspace.Kind == WorkspaceKind.Coding &&
                    string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static List<AppServerThreadSummary> FilterProjectInstructionHelperThreads(
            IEnumerable<AppServerThreadSummary> threads,
            ISet<string> helperThreadIds)
        {
            var filtered = new List<AppServerThreadSummary>();
            if (threads == null)
            {
                return filtered;
            }

            if (helperThreadIds == null || helperThreadIds.Count == 0)
            {
                filtered.AddRange(threads);
                return filtered;
            }

            foreach (var thread in threads)
            {
                if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId))
                {
                    continue;
                }

                if (helperThreadIds.Contains(thread.ThreadId))
                {
                    continue;
                }

                filtered.Add(thread);
            }

            return filtered;
        }
    }
}
