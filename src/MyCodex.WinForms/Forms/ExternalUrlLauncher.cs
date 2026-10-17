using System;
using System.Diagnostics;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class ExternalUrlLauncher
    {
        public static bool TryNormalizeAllowedUrl(string url, out string normalizedUrl)
        {
            normalizedUrl = null;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            Uri uri;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
            {
                return false;
            }

            if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || IsLocalHttp(uri))
            {
                normalizedUrl = uri.AbsoluteUri;
                return true;
            }

            return false;
        }

        public static void Open(string url)
        {
            string normalizedUrl;
            if (!TryNormalizeAllowedUrl(url, out normalizedUrl))
            {
                throw new InvalidOperationException(UiLocalization.Text(
                    "ExternalUrlLauncher.Error.UrlNotAllowed",
                    "Only HTTPS URLs and local HTTP callback URLs can be opened automatically."));
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = normalizedUrl,
                UseShellExecute = true
            });
        }

        private static bool IsLocalHttp(Uri uri)
        {
            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase));
        }
    }
}
