using System.Collections.Generic;

namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteMessageSendRequest
    {
        public CodingRemoteMessageSendRequest()
        {
            InputItems = new List<CodingRemoteInputItem>();
        }

        public string SessionId { get; set; }

        public string Text { get; set; }

        public string DeviceId { get; set; }

        public string OperationId { get; set; }

        public List<CodingRemoteInputItem> InputItems { get; set; }
    }
}
