using System.Collections.Generic;

namespace MyCodex.Application.RemoteRelay
{
    public sealed class RemoteRelayEvent
    {
        public RemoteRelayEvent()
        {
            SchemaVersion = 1;
            PayloadType = RemoteRelayPayloadTypes.Event;
            Payload = new Dictionary<string, object>();
        }

        public int SchemaVersion { get; set; }

        public string PayloadType { get; set; }

        public string EventType { get; set; }

        public string RequestId { get; set; }

        public string OperationId { get; set; }

        public Dictionary<string, object> Payload { get; set; }
    }
}
