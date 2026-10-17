using System.Collections.Generic;

namespace MyCodex.Application.CodingRemote
{
    public static class CodingRemoteWorkspaceActions
    {
        public const string Archive = "archive";
        public const string Delete = "delete";
    }

    public sealed class CodingRemoteWorkspaceOperationRequest
    {
        public string WorkspaceId { get; set; }

        public string Action { get; set; }

        public bool DeleteLocalHistory { get; set; }
    }

    public sealed class CodingRemoteWorkspaceOperationResult
    {
        public CodingRemoteWorkspaceOperationResult()
        {
            AffectedSessionIds = new List<string>();
        }

        public string WorkspaceId { get; set; }

        public string Action { get; set; }

        public string RuntimeProfileId { get; set; }

        public bool DeleteLocalHistory { get; set; }

        public int ProcessedCount { get; set; }

        public bool Removed { get; set; }

        public List<string> AffectedSessionIds { get; set; }

        public string StatusText { get; set; }
    }
}
