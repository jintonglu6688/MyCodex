using Microsoft.Win32;

namespace MyCodex.WinForms.Theming
{
    internal static class SystemThemeDetector
    {
        private const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightThemeName = "AppsUseLightTheme";

        public static bool UsesDarkTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PersonalizePath, false))
                {
                    var value = key != null ? key.GetValue(AppsUseLightThemeName) : null;
                    return value is int intValue && intValue == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
