using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerGuardianDeniedActionApprovalRequest
    {
        public string ThreadId { get; set; }

        public IDictionary<string, object> Event { get; set; }

        public AppServerGuardianApprovalReviewActionSummary Action { get; set; }

        public string RiskLevel { get; set; }

        public string Rationale { get; set; }
    }
}
