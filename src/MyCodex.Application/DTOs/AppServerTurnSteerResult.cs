namespace MyCodex.Application.DTOs
{
    public sealed class AppServerTurnSteerResult
    {
        public bool Attempted { get; set; }

        public bool Accepted { get; set; }

        public string TurnId { get; set; }

        public string ExpectedTurnId { get; set; }

        public string FallbackReason { get; set; }

        public static AppServerTurnSteerResult NotAttempted(string expectedTurnId, string reason)
        {
            return new AppServerTurnSteerResult
            {
                Attempted = false,
                Accepted = false,
                ExpectedTurnId = expectedTurnId,
                FallbackReason = reason
            };
        }

        public static AppServerTurnSteerResult Fallback(string expectedTurnId, string reason)
        {
            return new AppServerTurnSteerResult
            {
                Attempted = true,
                Accepted = false,
                ExpectedTurnId = expectedTurnId,
                FallbackReason = reason
            };
        }

        public static AppServerTurnSteerResult AcceptedResult(string expectedTurnId, string turnId)
        {
            return new AppServerTurnSteerResult
            {
                Attempted = true,
                Accepted = true,
                ExpectedTurnId = expectedTurnId,
                TurnId = turnId
            };
        }
    }
}
