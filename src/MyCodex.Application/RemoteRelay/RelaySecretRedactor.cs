using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MyCodex.Application.RemoteRelay
{
    public static class RelaySecretRedactor
    {
        private const string Replacement = "[REDACTED]";

        public static string Redact(string text)
        {
            return Redact(text, null);
        }

        public static string Redact(string text, IEnumerable<string> knownSecrets)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var redacted = text;
            redacted = Regex.Replace(redacted, "(Authorization\\s*[:=]\\s*Bearer\\s+)[^\\s,;]+", "$1" + Replacement, RegexOptions.IgnoreCase);
            redacted = Regex.Replace(redacted, "((?:tenantSecret|deviceToken|oneTimePairingToken)\\s*[:=]\\s*)[^\\s,;]+", "$1" + Replacement, RegexOptions.IgnoreCase);

            if (knownSecrets != null)
            {
                foreach (var secret in knownSecrets)
                {
                    if (string.IsNullOrWhiteSpace(secret))
                    {
                        continue;
                    }

                    redacted = redacted.Replace(secret, Replacement);
                }
            }

            return redacted;
        }
    }
}

