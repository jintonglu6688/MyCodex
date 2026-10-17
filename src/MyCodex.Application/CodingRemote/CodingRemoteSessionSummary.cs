using System;
using System.Collections.Generic;

namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteSessionSummary
    {
        public CodingRemoteSessionSummary()
        {
            SubAgents = new List<CodingRemoteSessionSummary>();
        }

        public string SessionId { get; set; }

        public string WorkspaceId { get; set; }

        public string Title { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public string BranchKind { get; set; }

        public int SubAgentCount { get; set; }

        public List<CodingRemoteSessionSummary> SubAgents { get; set; }

        public string SubAgentName { get; set; }

        public string SubAgentRole { get; set; }

        public string LastMessagePreview { get; set; }

        public string State { get; set; }
    }

    public static class CodingRemoteSessionBranchKinds
    {
        public const string Mainline = "mainline";
        public const string Fork = "fork";
        public const string Review = "review";
    }
}
