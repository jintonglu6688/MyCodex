using System;
using System.Collections.Generic;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class ModeWorkspaceProjection
    {
        internal static IReadOnlyList<WorkspaceInfo> Build(
            IEnumerable<WorkspaceInfo> workspaces,
            AppInteractionMode mode,
            bool showUnavailable,
            Func<WorkspaceInfo, bool> isCompatible,
            Func<WorkspaceInfo, bool> isAvailableFromCache)
        {
            var result = new List<WorkspaceInfo>();
            if (workspaces == null)
            {
                return result;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace == null ||
                    !IsWorkspaceInMode(workspace, mode) ||
                    (isCompatible != null && !isCompatible(workspace)) ||
                    (!showUnavailable &&
                     isAvailableFromCache != null &&
                     !isAvailableFromCache(workspace)))
                {
                    continue;
                }

                result.Add(workspace);
            }

            return result;
        }

        private static bool IsWorkspaceInMode(WorkspaceInfo workspace, AppInteractionMode mode)
        {
            return mode == AppInteractionMode.Office
                ? workspace.Kind == WorkspaceKind.Office
                : workspace.Kind == WorkspaceKind.Coding;
        }
    }
}
