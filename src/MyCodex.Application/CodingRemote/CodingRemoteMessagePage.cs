using System.Collections.Generic;

namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteMessagePage
    {
        public CodingRemoteMessagePage()
        {
            Messages = new List<CodingRemoteMessageItem>();
        }

        public string SessionId { get; set; }

        public List<CodingRemoteMessageItem> Messages { get; }

        public string NextCursor { get; set; }

        public string OlderCursor { get; set; }

        public string NewerCursor { get; set; }

        public bool HasOlder { get; set; }

        public bool HasNewer { get; set; }

        public bool TailConnected { get; set; }

        public string TailMessageId { get; set; }

        public string LatestAssistantMessageId { get; set; }

        public string HistoryRevision { get; set; }

        public bool AnchorFound { get; set; }

        public string ResolvedAnchorMessageId { get; set; }

        public bool IsGenerating { get; set; }
    }
}
