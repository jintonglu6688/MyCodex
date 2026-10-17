using System.Collections.Generic;

namespace MyCodex.Application.RemoteRelay
{
    public sealed class RemoteRelayCommand
    {
        public RemoteRelayCommand()
        {
            SchemaVersion = 1;
            PayloadType = RemoteRelayPayloadTypes.Command;
            Payload = new Dictionary<string, object>();
        }

        public int SchemaVersion { get; set; }

        public string PayloadType { get; set; }

        public string RequestId { get; set; }

        public string CommandType { get; set; }

        public string DeviceId { get; set; }

        public string OperationId { get; set; }

        public Dictionary<string, object> Payload { get; set; }
    }
}
