using System;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerConversationSummary
    {
        public string ThreadId { get; set; }

        public string Path { get; set; }

        public string Preview { get; set; }

        public DateTime? Timestamp { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public string ModelProviderId { get; set; }

        public string WorkingDirectory { get; set; }

        public string CliVersion { get; set; }

        public string Source { get; set; }

        public WorkspaceGitInfoSnapshot GitInfo { get; set; }
    }
}
