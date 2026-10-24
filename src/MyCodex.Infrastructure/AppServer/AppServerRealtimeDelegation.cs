using System;
using System.Net;

namespace MyCodex.Infrastructure.AppServer
{
    // Voice hands spoken requests to Codex in a <realtime_delegation> envelope; history shows only the request, like the official TUI.
    internal static class AppServerRealtimeDelegation
    {
        private const string Envelope = "<realtime_delegation>";
        private const string InputStart = "<input>";
        private const string InputEnd = "</input>";

        internal static bool TryUnwrap(string text, out string request)
        {
            request = null;
            if (text == null || !text.TrimStart().StartsWith(Envelope, StringComparison.Ordinal))
            {
                return false;
            }

            var start = text.IndexOf(InputStart, StringComparison.Ordinal);
            var end = start < 0 ? -1 : text.IndexOf(InputEnd, start, StringComparison.Ordinal);
            request = end < 0
                ? string.Empty
                : WebUtility.HtmlDecode(text.Substring(start + InputStart.Length, end - start - InputStart.Length)).Trim();
            return true;
        }
    }
}
