using System;

namespace MyCodex.Application.Services
{
    public sealed class SendOperationContext
    {
        public SendOperationContext(
            string runtimeSource,
            string runtimeProfileId,
            string workspaceId,
            string sessionId)
        {
            RuntimeSource = Normalize(runtimeSource);
            RuntimeProfileId = Normalize(runtimeProfileId);
            WorkspaceId = Normalize(workspaceId);
            SessionId = Normalize(sessionId);
        }

        public string RuntimeSource { get; }

        public string RuntimeProfileId { get; }

        public string WorkspaceId { get; }

        public string SessionId { get; }

        public bool Matches(
            string runtimeSource,
            string runtimeProfileId,
            string workspaceId,
            string sessionId)
        {
            return string.Equals(RuntimeSource, Normalize(runtimeSource), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(RuntimeProfileId, Normalize(runtimeProfileId), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(WorkspaceId, Normalize(workspaceId), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(SessionId, Normalize(sessionId), StringComparison.OrdinalIgnoreCase);
        }

        public bool MatchesRuntime(
            string runtimeSource,
            string runtimeProfileId)
        {
            return string.Equals(RuntimeSource, Normalize(runtimeSource), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(RuntimeProfileId, Normalize(runtimeProfileId), StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
