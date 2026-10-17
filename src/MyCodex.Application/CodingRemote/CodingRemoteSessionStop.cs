namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteSessionStopRequest
    {
        public string SessionId { get; set; }
    }

    public sealed class CodingRemoteSessionStopResult
    {
        public string SessionId { get; set; }

        public string WorkspaceId { get; set; }

        public bool Stopped { get; set; }

        public string StatusText { get; set; }
    }
}
