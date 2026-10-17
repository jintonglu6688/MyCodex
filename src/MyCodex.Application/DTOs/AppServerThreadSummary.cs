using System;

namespace MyCodex.Application.DTOs
{
    public class AppServerThreadSummary
    {
        public string ThreadId { get; set; }

        public string Title { get; set; }

        public string Preview { get; set; }

        public string ModelProviderId { get; set; }

        public string WorkingDirectory { get; set; }

        public string Source { get; set; }

        public string ThreadSource { get; set; }

        public string ParentThreadId { get; set; }

        public string SubAgentSourceKind { get; set; }

        public bool ParentThreadIdConflict { get; set; }

        public AppServerThreadStatus ThreadStatus { get; set; }

        public int? SubagentDepth { get; set; }

        public string AgentNickname { get; set; }

        public string AgentRole { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public string SessionPath { get; set; }
    }
}
