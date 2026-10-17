using System;

namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteWorkspaceSummary
    {
        public string WorkspaceId { get; set; }

        public string DisplayName { get; set; }

        public string Path { get; set; }

        public DateTime LastUsedAt { get; set; }

        public string Kind { get; set; }
    }
}
