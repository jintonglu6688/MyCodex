using System;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Infrastructure.RemoteRelay
{
    internal static class RemoteRelayDiagnostics
    {
        internal static string ShortId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            var normalized = value.Trim();
            return normalized.Length <= 10 ? normalized : normalized.Substring(0, 10);
        }

        internal static string ExceptionSummary(Exception exception, params string[] knownSecrets)
        {
            if (exception == null)
            {
                return "Unknown";
            }

            var message = RelaySecretRedactor.Redact(exception.Message ?? string.Empty, knownSecrets);
            if (message.Length > 300)
            {
                message = message.Substring(0, 300);
            }

            return exception.GetType().Name + (string.IsNullOrWhiteSpace(message) ? string.Empty : ": " + message);
        }
    }
}
