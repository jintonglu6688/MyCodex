using System;
using System.Diagnostics;
using System.Windows.Forms;
using MyCodex.Shared.Constants;
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

        public static string RequireAllowedUrl(string url)
        {
            string normalizedUrl;
            if (!TryNormalizeAllowedUrl(url, out normalizedUrl))
            {
                throw new InvalidOperationException(UiLocalization.Text(
                    "ExternalUrlLauncher.Error.UrlNotAllowed",
                    "Only HTTPS URLs and local HTTP callback URLs can be opened automatically."));
            }

            return normalizedUrl;
        }

        public static void Open(string url)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = RequireAllowedUrl(url),
                UseShellExecute = true
            });
        }

        // Links in message text open only when the user activates them, so plain HTTP and mailto are allowed too.
        public static bool IsMessageLink(Uri uri)
        {
            return uri != null &&
                   uri.IsAbsoluteUri &&
                   (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase));
        }

        public static void OpenMessageLink(IWin32Window owner, string url)
        {
            OpenMessageLink(
                url,
                link => Process.Start(new ProcessStartInfo { FileName = link, UseShellExecute = true }),
                message => MessageBox.Show(owner, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning));
        }

        // A failure must be reported: a screen reader user cannot tell that nothing happened otherwise.
        internal static void OpenMessageLink(string url, Action<string> start, Action<string> reportError)
        {
            Uri uri;
            if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out uri) || !IsMessageLink(uri))
            {
                reportError(UiLocalization.Text(
                    "ExternalUrlLauncher.Error.MessageLinkNotAllowed",
                    "Only HTTP, HTTPS, and email links can be opened from a message."));
                return;
            }

            try
            {
                start(uri.AbsoluteUri);
            }
            catch (Exception ex)
            {
                reportError(ex.Message);
            }
        }

        private static bool IsLocalHttp(Uri uri)
        {
            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
        }
    }
}
