namespace MyCodex.WinForms.Forms
{
    internal static class TargetTrayTextFormatter
    {
        internal static string BuildTrayTooltip(string appName, string target, string status)
        {
            // NotifyIcon.Text has a 63-character ceiling; preserve the state when a target name is long.
            var suffix = " — " + status + " — " + appName;
            if (suffix.Length > 47) suffix = " — " + Truncate(status, 30) + " — " + Truncate(appName, 10);
            return Truncate(target, 63 - suffix.Length) + suffix;
        }

        private static string Truncate(string text, int length)
        {
            text = text ?? string.Empty;
            if (text.Length <= length) return text;
            if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
            return text.Substring(0, length);
        }
    }
}
