namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteMessageContentPage
    {
        public string SessionId { get; set; }

        public string MessageId { get; set; }

        public string ContentVersion { get; set; }

        public string Text { get; set; }

        public string NextCursor { get; set; }

        public long LoadedUtf8Bytes { get; set; }

        public long TotalUtf8Bytes { get; set; }

        public bool Complete { get; set; }
    }
}
