using System;
using System.Globalization;
using System.Resources;

namespace MyCodex.Shared.Localization
{
    public static class SharedLocalization
    {
        private static readonly ResourceManager ResourceManager =
            new ResourceManager("MyCodex.Shared.Resources.InternalStrings", typeof(SharedLocalization).Assembly);

        public static string Text(string key, string fallback = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return fallback ?? string.Empty;
            }

            try
            {
                var value = ResourceManager.GetString(key, CultureInfo.CurrentUICulture);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch
            {
                // Ignore lookup failures and use fallback.
            }

            return fallback ?? key;
        }
    }
}
