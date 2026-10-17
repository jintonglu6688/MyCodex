namespace MyCodex.Domain.Entities
{
    public static class WindowsShellPreferences
    {
        public const string Auto = "auto";
        public const string PowerShell7 = "powershell7";
        public const string WindowsPowerShell = "windowsPowerShell";

        public static string Normalize(string value)
        {
            if (string.Equals(value, PowerShell7, System.StringComparison.OrdinalIgnoreCase))
            {
                return PowerShell7;
            }

            if (string.Equals(value, WindowsPowerShell, System.StringComparison.OrdinalIgnoreCase))
            {
                return WindowsPowerShell;
            }

            return Auto;
        }
    }
}
