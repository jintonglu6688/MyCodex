namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteMessagesListRequest
    {
        public string SessionId { get; set; }

        public string BeforeCursor { get; set; }

        public string AfterCursor { get; set; }

        public string AnchorMessageId { get; set; }

        public int PagingVersion { get; set; }

        public string KnownHistoryRevision { get; set; }

        public int Limit { get; set; }
    }
}
