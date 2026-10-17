namespace MyCodex.Application.CodingRemote
{
    public static class CodingRemoteSessionThreadActions
    {
        public const string Fork = "fork";
        public const string Compact = "compact";
    }

    public static class CodingRemoteSessionThreadOperationModes
    {
        public const string Full = "full";
        public const string FromTurn = "fromTurn";
    }

    public sealed class CodingRemoteSessionThreadOperationRequest
    {
        public string SessionId { get; set; }

        public string Action { get; set; }

        public string Mode { get; set; }

        public string TurnId { get; set; }

        public string Title { get; set; }

        public bool SwitchToCreatedSession { get; set; }
    }

    public sealed class CodingRemoteSessionThreadOperationResult
    {
        public string SessionId { get; set; }

        public string WorkspaceId { get; set; }

        public string Action { get; set; }

        public string Mode { get; set; }

        public string TurnId { get; set; }


        public bool Accepted { get; set; }

        public CodingRemoteSessionSummary CreatedSession { get; set; }

        public bool SwitchToCreatedSession { get; set; }

        public string StatusText { get; set; }
    }
}
