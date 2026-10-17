using System;

namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteMessageItem
    {
        public string MessageId { get; set; }

        public string SessionId { get; set; }

        public string Role { get; set; }

        public DateTime CreatedAt { get; set; }

        public string Text { get; set; }

        public bool ContentTruncated { get; set; }

        public long ContentUtf8ByteCount { get; set; }

        public string ContentVersion { get; set; }

        public string Status { get; set; }

        public string ThreadId { get; set; }

        public string TurnId { get; set; }

        public int TurnIndex { get; set; }

        public int TurnCount { get; set; }

        public DateTime TurnStartedAt { get; set; }

        public string TurnPreview { get; set; }

        public CodingRemoteMessageActions Actions { get; set; }

        public CodingRemoteMessageOperationPreview OperationPreview { get; set; }
    }
}
