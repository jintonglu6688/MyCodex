using System;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class SessionCreationWorkspaceResolver
    {
        internal static WorkspaceInfo Resolve(
            AppInteractionMode mode,
            WorkspaceInfo currentWorkspace,
            Func<WorkspaceInfo> ensureOfficeWorkspace)
        {
            if (mode == AppInteractionMode.Office)
            {
                if (currentWorkspace != null && currentWorkspace.Kind == WorkspaceKind.Office)
                {
                    currentWorkspace.Kind = WorkspaceKind.Office;
                    return currentWorkspace;
                }

                var officeWorkspace = ensureOfficeWorkspace != null ? ensureOfficeWorkspace() : currentWorkspace;
                if (officeWorkspace != null)
                {
                    officeWorkspace.Kind = WorkspaceKind.Office;
                }

                return officeWorkspace;
            }

            return currentWorkspace != null && currentWorkspace.Kind == WorkspaceKind.Coding
                ? currentWorkspace
                : null;
        }
    }
}
