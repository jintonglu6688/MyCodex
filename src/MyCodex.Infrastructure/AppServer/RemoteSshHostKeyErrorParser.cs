using System;
using System.Text.RegularExpressions;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.AppServer
{
    public static class RemoteSshHostKeyErrorParser
    {
        public static bool TryExtractFingerprint(string message, out string hostKey)
        {
            hostKey = null;
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            if (message.IndexOf("host key", StringComparison.OrdinalIgnoreCase) < 0 &&
                message.IndexOf("fingerprint", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            var match = Regex.Match(message, @"SHA256:[A-Za-z0-9+/=]+", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            hostKey = match.Value.Trim().TrimEnd('.', ',', ';', ')', ']');
            return !string.IsNullOrWhiteSpace(hostKey);
        }
    }

    public static class RemoteSshHostKeyTrustPlanner
    {
        public static bool ShouldPromptForTrust(
            RemoteMacSshConfigSnapshot config,
            string errorMessage,
            out string hostKey)
        {
            hostKey = null;
            if (config == null ||
                config.AuthenticationMode != RemoteSshAuthenticationMode.Password)
            {
                return false;
            }

            if (!RemoteSshHostKeyErrorParser.TryExtractFingerprint(errorMessage, out hostKey))
            {
                return false;
            }

            if (string.Equals(config.HostKeyFingerprint, hostKey, StringComparison.Ordinal))
            {
                hostKey = null;
                return false;
            }

            return true;
        }
    }
}
