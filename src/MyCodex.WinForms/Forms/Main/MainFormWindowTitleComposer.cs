namespace MyCodex.WinForms.Forms
{
    internal static class MainFormWindowTitleComposer
    {
        internal static string BuildWindowTitle(string appDisplayName, string statusText, string remoteRuntimeName)
        {
            return BuildWindowTitle(appDisplayName, statusText, remoteRuntimeName, null);
        }

        internal static string BuildWindowTitle(string appDisplayName, string statusText, string remoteRuntimeName, string modeText)
        {
            var status = statusText;
            if (!string.IsNullOrWhiteSpace(remoteRuntimeName))
            {
                status = string.IsNullOrWhiteSpace(status)
                    ? remoteRuntimeName.Trim()
                    : status.Trim() + " - " + remoteRuntimeName.Trim();
            }

            var title = appDisplayName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(modeText))
            {
                title = string.IsNullOrWhiteSpace(title)
                    ? modeText.Trim()
                    : title.Trim() + " - " + modeText.Trim();
            }

            return string.IsNullOrWhiteSpace(status)
                ? title
                : status.Trim() + " - " + title;
        }

        internal static string AddTargetPrefix(string title, string targetName)
        {
            return string.IsNullOrWhiteSpace(targetName) ? title ?? string.Empty : targetName.Trim() + " - " + title;
        }

        internal static string BuildRuntimeFileWindowTitle(string runtimeName, string purpose, string defaultPurpose)
        {
            var normalizedPurpose = string.IsNullOrWhiteSpace(purpose)
                ? defaultPurpose
                : purpose.Trim();
            return AddTargetPrefix(normalizedPurpose, runtimeName);
        }
    }
}
