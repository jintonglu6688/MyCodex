using System;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class OfficeWorkspaceAvailabilityPlanner
    {
        internal static bool IsUnavailable(
            WorkspaceInfo workspace,
            string activeOfficeRuntimeScopeId,
            string activeOfficeRootError)
        {
            if (workspace == null ||
                workspace.Kind != WorkspaceKind.Office ||
                string.IsNullOrWhiteSpace(workspace.Id) ||
                string.IsNullOrWhiteSpace(activeOfficeRuntimeScopeId) ||
                string.IsNullOrWhiteSpace(activeOfficeRootError))
            {
                return false;
            }

            var activeWorkspaceId = "office:" + activeOfficeRuntimeScopeId.Trim();
            return string.Equals(workspace.Id, activeWorkspaceId, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ResolveForProjection(
            WorkspaceInfo workspace,
            string activeOfficeRuntimeScopeId,
            string activeOfficeRootError,
            bool? cachedAvailability)
        {
            if (workspace == null ||
                IsUnavailable(workspace, activeOfficeRuntimeScopeId, activeOfficeRootError))
            {
                return false;
            }

            return !cachedAvailability.HasValue || cachedAvailability.Value;
        }
    }
}
