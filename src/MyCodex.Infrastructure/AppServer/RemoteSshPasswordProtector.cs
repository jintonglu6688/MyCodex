using System;
using System.Security.Cryptography;
using System.Text;

namespace MyCodex.Infrastructure.AppServer
{
    public static class RemoteSshPasswordProtector
    {
        public static string Protect(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return null;
            }

            var bytes = Encoding.UTF8.GetBytes(password);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public static string Unprotect(string protectedPassword)
        {
            if (string.IsNullOrWhiteSpace(protectedPassword))
            {
                return null;
            }

            var protectedBytes = Convert.FromBase64String(protectedPassword);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
