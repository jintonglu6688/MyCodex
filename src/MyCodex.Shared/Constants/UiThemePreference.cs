using System;

namespace MyCodex.Shared.Constants
{
    public static class UiThemePreference
    {
        public const string Auto = "auto";
        public const string Light = "light";
        public const string Dark = "dark";

        public static string Normalize(string preference)
        {
            var value = string.IsNullOrWhiteSpace(preference) ? Auto : preference.Trim();
            if (string.Equals(value, Light, StringComparison.OrdinalIgnoreCase))
            {
                return Light;
            }

            if (string.Equals(value, Dark, StringComparison.OrdinalIgnoreCase))
            {
                return Dark;
            }

            return Auto;
        }
    }
}
