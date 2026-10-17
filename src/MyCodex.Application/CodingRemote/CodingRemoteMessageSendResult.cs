namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteMessageSendResult
    {
        public string SessionId { get; set; }

        public string OperationId { get; set; }

        public bool Accepted { get; set; }

        public bool RecreatedThread { get; set; }

        public string DispatchMode { get; set; }
    }
}
