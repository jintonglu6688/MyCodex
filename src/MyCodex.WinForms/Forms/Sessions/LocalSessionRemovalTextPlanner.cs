using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal enum LocalSessionRemovalMode
    {
        LocalOnly,
        PermanentDelete,
        Archive
    }

    internal static class LocalSessionRemovalTextPlanner
    {
        internal static string BuildPermanentDeletionResultText(SessionPermanentDeletionResult result,
            IReadOnlyList<string> deletedFiles, IReadOnlyList<string> failedFiles)
        {
            var summary = string.Format(UiLocalization.Text("PermanentDeletion.Result.Template",
                "Deleted {0}: {2}{4}Failed or unconfirmed {1}: {3}"),
                result.DeletedThreadIds.Count + deletedFiles.Count, result.FailedThreadIds.Count + failedFiles.Count,
                string.Join(", ", result.DeletedThreadIds.Concat(deletedFiles)),
                string.Join(", ", result.FailedThreadIds.Concat(failedFiles)), Environment.NewLine);
            var reasons = result.FailureReasons.Select(pair => pair.Value + " [" + pair.Key + "]").ToList();
            reasons.Add(summary);
            return string.Join(Environment.NewLine + Environment.NewLine, reasons);
        }

        internal static MessageBoxIcon GetConfirmationIcon(LocalSessionRemovalMode removalMode)
        {
            return removalMode == LocalSessionRemovalMode.LocalOnly ? MessageBoxIcon.Question : MessageBoxIcon.Warning;
        }

        internal static string BuildWorkspaceConfirmation(WorkspaceInfo workspace, int sessionCount, LocalSessionRemovalMode removalMode)
        {
            var workspaceName = workspace != null
                ? workspace.Name
                : UiLocalization.Text("MainForm.Value.CurrentWorkspace", "Current workspace");
            switch (removalMode)
            {
                case LocalSessionRemovalMode.Archive:
                    return string.Format(
                        UiLocalization.Text(
                            "MainForm.Confirm.ArchiveWorkspaceSessions.Template",
                            "Archive {1} session(s) under workspace \"{0}\"?{2}{2}After archiving, these sessions are removed from active Codex threads but can still be restored."),
                        workspaceName,
                        sessionCount,
                        Environment.NewLine);
                case LocalSessionRemovalMode.PermanentDelete:
                    return string.Format(
                        UiLocalization.Text(
                            "MainForm.Confirm.DeleteWorkspaceSessionFiles.Template",
                            "Permanently delete {1} session file(s) under workspace \"{0}\"?{2}{2}This tries to remove local thread files; if files are already missing, only MyCodex history records are cleaned."),
                        workspaceName,
                        sessionCount,
                        Environment.NewLine);
                default:
                    return string.Format(
                        UiLocalization.Text(
                            "MainForm.Confirm.RemoveWorkspaceFromMyCodex.Template",
                            "Remove workspace \"{0}\" and its {1} session(s) from MyCodex only?{2}{2}This does not affect Codex CLI, official app, or underlying real threads."),
                        workspaceName,
                        sessionCount,
                        Environment.NewLine);
            }
        }

        internal static string BuildSessionConfirmation(ChatSession session, LocalSessionRemovalMode removalMode)
        {
            var sessionTitle = session != null
                ? session.Title
                : UiLocalization.Text("MainForm.Value.CurrentSession", "Current session");
            switch (removalMode)
            {
                case LocalSessionRemovalMode.Archive:
                    return string.Format(
                        UiLocalization.Text(
                            "MainForm.Confirm.ArchiveSession.Template",
                            "Archive session \"{0}\"?{1}{1}After archiving, this session is removed from active Codex threads but can still be restored."),
                        sessionTitle,
                        Environment.NewLine);
                case LocalSessionRemovalMode.PermanentDelete:
                    return string.Format(
                        UiLocalization.Text(
                            "MainForm.Confirm.DeleteSessionThreadFile.Template",
                            "Permanently delete thread file for session \"{0}\"?{1}{1}This tries to delete local thread files; if files are already missing, only MyCodex history records are cleaned."),
                        sessionTitle,
                        Environment.NewLine);
                default:
                    return string.Format(
                        UiLocalization.Text(
                            "MainForm.Confirm.RemoveSessionFromMyCodex.Template",
                            "Remove session \"{0}\" from MyCodex only?{1}{1}This does not affect Codex CLI, official app, or underlying real threads."),
                        sessionTitle,
                        Environment.NewLine);
            }
        }

        internal static string BuildWorkspaceProgressText(LocalSessionRemovalMode removalMode, string workspaceName)
        {
            switch (removalMode)
            {
                case LocalSessionRemovalMode.Archive:
                    return string.Format(
                        UiLocalization.Text("MainForm.Status.ArchivingWorkspaceSessions.Template", "Archiving sessions under workspace \"{0}\"..."),
                        workspaceName);
                case LocalSessionRemovalMode.PermanentDelete:
                    return string.Format(
                        UiLocalization.Text("MainForm.Status.DeletingWorkspaceSessionFiles.Template", "Permanently deleting session files under workspace \"{0}\"..."),
                        workspaceName);
                default:
                    return string.Format(
                        UiLocalization.Text("MainForm.Status.RemovingWorkspaceFromMyCodex.Template", "Removing workspace \"{0}\" from MyCodex..."),
                        workspaceName);
            }
        }

        internal static string BuildSessionProgressText(LocalSessionRemovalMode removalMode, string sessionTitle)
        {
            switch (removalMode)
            {
                case LocalSessionRemovalMode.Archive:
                    return string.Format(
                        UiLocalization.Text("MainForm.Status.ArchivingSession.Template", "Archiving session \"{0}\"..."),
                        sessionTitle);
                case LocalSessionRemovalMode.PermanentDelete:
                    return string.Format(
                        UiLocalization.Text("MainForm.Status.DeletingSessionFile.Template", "Permanently deleting session \"{0}\"..."),
                        sessionTitle);
                default:
                    return string.Format(
                        UiLocalization.Text("MainForm.Status.RemovingSessionFromMyCodex.Template", "Removing session \"{0}\" from MyCodex..."),
                        sessionTitle);
            }
        }

        internal static string BuildWorkspaceCompletionStatus(LocalSessionRemovalMode removalMode, string workspaceName, int processedCount)
        {
            switch (removalMode)
            {
                case LocalSessionRemovalMode.Archive:
                    return string.Format(
                        UiLocalization.Text("MainForm.Status.WorkspaceSessionsArchived.Template", "Archived {1} session(s) under workspace \"{0}\"."),
                        workspaceName,
                        processedCount);
                case LocalSessionRemovalMode.PermanentDelete:
                    return string.Format(
                        UiLocalization.Text("MainForm.Status.WorkspaceSessionFilesDeleted.Template", "Permanently deleted {1} session file(s) under workspace \"{0}\"."),
                        workspaceName,
                        processedCount);
                default:
                    return string.Format(
                        UiLocalization.Text("MainForm.Status.WorkspaceRemovedFromMyCodex.Template", "Removed workspace \"{0}\" from MyCodex."),
                        workspaceName);
            }
        }

        internal static string BuildSessionCompletionStatus(LocalSessionRemovalMode removalMode, string sessionTitle)
        {
            switch (removalMode)
            {
                case LocalSessionRemovalMode.Archive:
                    return string.Format(
                        UiLocalization.Text("MainForm.Status.SessionArchived.Template", "Archived session \"{0}\"."),
                        sessionTitle);
                case LocalSessionRemovalMode.PermanentDelete:
                    return string.Format(
                        UiLocalization.Text("MainForm.Status.SessionPermanentlyDeleted.Template", "Permanently deleted session \"{0}\"."),
                        sessionTitle);
                default:
                    return string.Format(
                        UiLocalization.Text("MainForm.Status.SessionRemovedFromMyCodex.Template", "Removed session \"{0}\" from MyCodex."),
                        sessionTitle);
            }
        }
    }
}
