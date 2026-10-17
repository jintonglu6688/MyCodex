using System;
using System.Globalization;
using System.Resources;
using System.Threading;

namespace MyCodex.WinForms.Localization
{
    internal static class UiLocalization
    {
        private static readonly ResourceManager ResourceManager =
            new ResourceManager("MyCodex.WinForms.Resources.Strings", typeof(UiLocalization).Assembly);
        private static readonly CultureInfo StartupSystemUICulture = CaptureStartupSystemUICulture();

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
                // Ignore localization lookup failures and fall back to caller-provided text.
            }

            return fallback ?? key;
        }

        public static string NormalizePreference(string preference)
        {
            if (string.IsNullOrWhiteSpace(preference))
            {
                return UiLanguagePreference.Auto;
            }

            var trimmed = preference.Trim();
            if (string.Equals(trimmed, UiLanguagePreference.ZhCn, StringComparison.OrdinalIgnoreCase))
            {
                return UiLanguagePreference.ZhCn;
            }

            if (string.Equals(trimmed, UiLanguagePreference.EnUs, StringComparison.OrdinalIgnoreCase))
            {
                return UiLanguagePreference.EnUs;
            }

            return UiLanguagePreference.Auto;
        }

        public static void ApplyPreferredCulture(string preference)
        {
            var culture = ResolveCulture(preference);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }

        private static CultureInfo ResolveCulture(string preference)
        {
            var normalized = NormalizePreference(preference);
            if (string.Equals(normalized, UiLanguagePreference.ZhCn, StringComparison.OrdinalIgnoreCase))
            {
                return new CultureInfo(UiLanguagePreference.ZhCn);
            }

            if (string.Equals(normalized, UiLanguagePreference.EnUs, StringComparison.OrdinalIgnoreCase))
            {
                return new CultureInfo(UiLanguagePreference.EnUs);
            }

            return ResolveSystemCulture();
        }

        private static CultureInfo ResolveSystemCulture()
        {
            var systemCulture = StartupSystemUICulture;
            if (systemCulture != null &&
                string.Equals(systemCulture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase))
            {
                return new CultureInfo(UiLanguagePreference.ZhCn);
            }

            return new CultureInfo(UiLanguagePreference.EnUs);
        }

        private static CultureInfo CaptureStartupSystemUICulture()
        {
            try
            {
                var currentCulture = CultureInfo.CurrentUICulture;
                if (currentCulture != null && !string.IsNullOrWhiteSpace(currentCulture.Name))
                {
                    return currentCulture;
                }
            }
            catch
            {
            }

            try
            {
                var installedCulture = CultureInfo.InstalledUICulture;
                if (installedCulture != null && !string.IsNullOrWhiteSpace(installedCulture.Name))
                {
                    return installedCulture;
                }
            }
            catch
            {
            }

            return new CultureInfo(UiLanguagePreference.EnUs);
        }
    }
}
