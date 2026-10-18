using System;
using System.Collections.Generic;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public static class WorkspaceGitMetadataRefreshPlanner
    {
        public static bool Apply(IList<WorkspaceInfo> recentWorkspaces, WorkspaceInfo currentWorkspace, WorkspaceInfo refreshedWorkspace)
        {
            if (refreshedWorkspace == null || string.IsNullOrWhiteSpace(refreshedWorkspace.Id))
            {
                return false;
            }

            var changed = false;
            if (IsSameWorkspace(currentWorkspace, refreshedWorkspace))
            {
                changed |= CopyGitMetadata(currentWorkspace, refreshedWorkspace);
            }

            if (recentWorkspaces == null)
            {
                return changed;
            }

            for (var i = 0; i < recentWorkspaces.Count; i++)
            {
                var workspace = recentWorkspaces[i];
                if (ReferenceEquals(workspace, currentWorkspace) || !IsSameWorkspace(workspace, refreshedWorkspace))
                {
                    continue;
                }

                changed |= CopyGitMetadata(workspace, refreshedWorkspace);
            }

            return changed;
        }

        private static bool IsSameWorkspace(WorkspaceInfo left, WorkspaceInfo right)
        {
            return left != null &&
                   right != null &&
                   !string.IsNullOrWhiteSpace(left.Id) &&
                   !string.IsNullOrWhiteSpace(right.Id) &&
                   string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CopyGitMetadata(WorkspaceInfo target, WorkspaceInfo source)
        {
            if (target == null || source == null)
            {
                return false;
            }

            if (string.Equals(target.GitBranch, source.GitBranch, StringComparison.Ordinal))
            {
                return false;
            }

            target.GitBranch = source.GitBranch;
            return true;
        }
    }
}
