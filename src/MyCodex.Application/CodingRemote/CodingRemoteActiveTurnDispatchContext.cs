using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteActiveTurnDispatchContext
    {
        public string SessionId { get; set; }

        public string OperationId { get; set; }

        public IReadOnlyCollection<AppServerUserInputItem> InputItems { get; set; }

        public ApprovalMode? ApprovalMode { get; set; }

        public string ModelId { get; set; }

        public string ReasoningEffort { get; set; }

        public AppServerCollaborationMode CollaborationMode { get; set; }

        public AppServerThreadToolContext ToolContext { get; set; }

        public string DispatchText { get; set; }

        public string PreviewText { get; set; }
    }
}
