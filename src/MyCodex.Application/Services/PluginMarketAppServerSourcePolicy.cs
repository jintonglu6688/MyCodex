using System;

namespace MyCodex.Application.Services
{
    public static class PluginMarketAppServerSourcePolicy
    {
        public static bool IsAllowed(string source)
        {
            Uri uri;
            return !string.IsNullOrWhiteSpace(source) &&
                Uri.TryCreate(source.Trim(), UriKind.Absolute, out uri) &&
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(uri.Host) &&
                string.IsNullOrEmpty(uri.UserInfo) &&
                string.IsNullOrEmpty(uri.Query) &&
                string.IsNullOrEmpty(uri.Fragment);
        }
    }
}
