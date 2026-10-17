using System;

namespace MyCodex.Application.Services
{
    public static class AuthenticationLoginUrl
    {
        public static bool IsBrowserAuthorization(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps && uri.Host == "auth.openai.com" &&
                uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
                uri.AbsolutePath == "/oauth/authorize" && uri.Query.Length > 1;
        }
    }
}
