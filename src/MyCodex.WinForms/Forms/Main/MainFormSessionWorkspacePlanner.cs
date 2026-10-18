using System;
using System.Collections.Generic;
using System.IO;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class MainFormSessionWorkspacePlanner
    {
        public static WorkspaceInfo ResolveWorkspace(
            AutomationRunSnapshot run,
            ChatSession session,
            string fallbackPath,
            Func<string, WorkspaceInfo> findWorkspaceById,
            Func<string, WorkspaceInfo> findWorkspaceByPath,
            Func<string, WorkspaceInfo> resolveOfficeWorkspace,
            Func<string, bool> isOfficePath)
        {
            if (session != null)
            {
                var sessionWorkspace = FindById(findWorkspaceById, session.WorkspaceId);
                if (sessionWorkspace != null)
                {
                    return sessionWorkspace;
                }
            }

            if (run != null)
            {
                var runWorkspace = FindById(findWorkspaceById, run.WorkspaceId);
                if (runWorkspace != null)
                {
                    return runWorkspace;
                }
            }

            var path = ResolveWorkspacePath(run, fallbackPath);
            if (string.IsNullOrWhiteSpace(path) || MainFormAutomationNavigationPlanner.IsRemotePath(path))
            {
                return null;
            }

            var existingWorkspace = findWorkspaceByPath == null ? null : findWorkspaceByPath(path);
            if (existingWorkspace != null)
            {
                return existingWorkspace;
            }

            if (isOfficePath != null && isOfficePath(path))
            {
                return resolveOfficeWorkspace == null ? null : resolveOfficeWorkspace(path);
            }

            return null;
        }

        public static AppInteractionMode ResolveInteractionMode(
            ChatSession session,
            WorkspaceInfo workspace,
            string workspacePath,
            Func<string, bool> isOfficePath)
        {
            if (session != null)
            {
                return session.Mode;
            }

            if (workspace != null && workspace.Kind == WorkspaceKind.Office)
            {
                return AppInteractionMode.Office;
            }

            return isOfficePath != null && isOfficePath(workspacePath)
                ? AppInteractionMode.Office
                : AppInteractionMode.Coding;
        }

        public static string ResolveWorkspacePath(AutomationRunSnapshot run, string fallbackPath)
        {
            if (run != null && !string.IsNullOrWhiteSpace(run.WorkspacePath))
            {
                return run.WorkspacePath.Trim();
            }

            return string.IsNullOrWhiteSpace(fallbackPath) ? null : fallbackPath.Trim();
        }

        public static WorkspaceInfo FindWorkspaceByPath(IEnumerable<WorkspaceInfo> workspaces, string workspacePath)
        {
            if (workspaces == null || string.IsNullOrWhiteSpace(workspacePath))
            {
                return null;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace == null || string.IsNullOrWhiteSpace(workspace.RootPath))
                {
                    continue;
                }

                if (ArePathsEqual(workspace.RootPath, workspacePath))
                {
                    return workspace;
                }
            }

            return null;
        }

        public static bool ArePathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static WorkspaceInfo FindById(Func<string, WorkspaceInfo> findWorkspaceById, string workspaceId)
        {
            return findWorkspaceById == null || string.IsNullOrWhiteSpace(workspaceId)
                ? null
                : findWorkspaceById(workspaceId);
        }
    }
}
