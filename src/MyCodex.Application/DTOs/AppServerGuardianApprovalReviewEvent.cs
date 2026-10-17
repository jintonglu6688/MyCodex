namespace MyCodex.Application.DTOs
{
    public sealed class AppServerGuardianApprovalReviewEvent
    {
        public string ThreadId { get; set; }

        public string TurnId { get; set; }

        public string ReviewId { get; set; }

        public string TargetItemId { get; set; }

        public string Status { get; set; }

        public string RiskLevel { get; set; }

        public string UserAuthorization { get; set; }

        public string Rationale { get; set; }

        public string DecisionSource { get; set; }

        public long? StartedAtMs { get; set; }

        public long? CompletedAtMs { get; set; }

        public bool IsCompleted { get; set; }

        public AppServerGuardianApprovalReviewActionSummary Action { get; set; }

        public AppServerGuardianDeniedActionApprovalRequest ApprovalRequest { get; set; }
    }
}
