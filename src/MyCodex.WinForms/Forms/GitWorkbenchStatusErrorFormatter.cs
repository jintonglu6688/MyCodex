using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class GitWorkbenchStatusErrorFormatter
    {
        internal static bool IsSafeDirectoryOwnershipError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.IndexOf("dubious ownership", System.StringComparison.OrdinalIgnoreCase) >= 0
                || (message.IndexOf("safe.directory", System.StringComparison.OrdinalIgnoreCase) >= 0
                    && message.IndexOf("ownership", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static string BuildDisplayMessage(string workspacePath, string statusErrorMessage)
        {
            if (!IsSafeDirectoryOwnershipError(statusErrorMessage))
            {
                return statusErrorMessage ?? string.Empty;
            }

            return string.Format(
                UiLocalization.Text(
                    "GitWorkbenchForm.Message.SafeDirectoryBlocked.Template",
                    "Git blocked this repository because its owner does not match the current Windows account.\r\n\r\nRepository: {0}\r\nRun this command, then refresh Git Workbench:\r\n\r\ngit config --global --add safe.directory {1}"),
                workspacePath ?? string.Empty,
                NormalizeGitPath(workspacePath));
        }

        internal static string BuildWorktreePlaceholderText(string workspacePath, string statusErrorMessage)
        {
            if (IsSafeDirectoryOwnershipError(statusErrorMessage))
            {
                return UiLocalization.Text(
                    "GitWorkbenchForm.Value.WorktreesBlockedBySafeDirectory",
                    "Git safe.directory is blocking worktree loading. Check the operation log for the command to run.");
            }

            return UiLocalization.Text(
                "GitWorkbenchForm.Value.WorktreesUnavailable",
                "Worktrees are unavailable while Git status cannot be read.");
        }

        private static string NormalizeGitPath(string workspacePath)
        {
            return string.IsNullOrWhiteSpace(workspacePath)
                ? string.Empty
                : workspacePath.Replace('\\', '/');
        }
    }
}
