using MyCodex.Domain.Enums;

namespace MyCodex.Application.DTOs
{
    public sealed class CustomApprovalPolicyEvaluation
    {
        public bool ShouldAutoApprove { get; set; }

        public CustomApprovalPermissionKind? Permission { get; set; }

        public CustomApprovalPolicySource Source { get; set; }

        public string MatchedCommandPrefix { get; set; }
    }
}
