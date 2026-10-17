namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteMessageContentRequest
    {
        public string SessionId { get; set; }

        public string MessageId { get; set; }

        public string ContentVersion { get; set; }

        public string Cursor { get; set; }
    }
}
