namespace MyCodex.Application.CodingRemote
{
    public static class CodingRemoteSessionActions
    {
        public const string Archive = "archive";
        public const string Delete = "delete";
        public const string Rename = "rename";
    }

    public sealed class CodingRemoteSessionOperationRequest
    {
        public string SessionId { get; set; }

        public string Action { get; set; }

        public bool DeleteLocalHistory { get; set; }

        public string Title { get; set; }
    }

    public sealed class CodingRemoteSessionOperationResult
    {
        public string SessionId { get; set; }

        public string WorkspaceId { get; set; }

        public string Action { get; set; }

        public string RuntimeProfileId { get; set; }

        public bool DeleteLocalHistory { get; set; }

        public int ProcessedCount { get; set; }

        public bool Removed { get; set; }

        public bool Updated { get; set; }

        public string Title { get; set; }

        public string StatusText { get; set; }
    }
}
