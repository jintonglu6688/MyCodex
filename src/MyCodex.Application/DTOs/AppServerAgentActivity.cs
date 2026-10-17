using System;
using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerThreadStatus
    {
        public string Type { get; set; }
        public IReadOnlyList<string> ActiveFlags { get; set; } = Array.Empty<string>();
    }

    public sealed class AppServerCollabAgentState
    {
        public string Status { get; set; }
        public string Message { get; set; }
    }

    public sealed class AppServerCollaborationActivity
    {
        public string SenderThreadId { get; set; }
        public IReadOnlyList<string> ReceiverThreadIds { get; set; } = Array.Empty<string>();
        public string Tool { get; set; }
        public string Status { get; set; }
        public IReadOnlyDictionary<string, AppServerCollabAgentState> AgentsStates { get; set; }
        public string Prompt { get; set; }
        public string RequestedModel { get; set; }
        public string RequestedReasoningEffort { get; set; }
    }

    public sealed class AppServerSubAgentActivity
    {
        public string AgentThreadId { get; set; }
        public string AgentPath { get; set; }
        public string Kind { get; set; }
    }
}
