using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace MyCodex.Application.RemoteRelay
{
    // The channel owns all outer-frame fields; this helper validates only authenticated business JSON.
    public static class RemoteRelayResponseEnvelopePlanner
    {
        public static bool TryDeserializeRemoteCommand(string json, string expectedDeviceId, out RemoteRelayCommand command)
        {
            command = null;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(expectedDeviceId))
            {
                return false;
            }

            try
            {
                var root = new JavaScriptSerializer
                {
                    MaxJsonLength = RemoteRelayPayloadJsonSerializer.MaxBusinessJsonCharacters
                }.DeserializeObject(json) as IDictionary<string, object>;
                object payloadType;
                object requestId;
                object deviceId;
                object commandType;
                if (root == null ||
                    !root.TryGetValue("payloadType", out payloadType) || !(payloadType is string) ||
                    !root.TryGetValue("requestId", out requestId) || !(requestId is string) ||
                    !root.TryGetValue("deviceId", out deviceId) || !(deviceId is string) ||
                    !root.TryGetValue("commandType", out commandType) || !(commandType is string))
                {
                    return false;
                }

                var parsed = RemoteRelayPayloadJsonSerializer.DeserializeCommand(json);
                if (parsed == null ||
                    !string.Equals(parsed.PayloadType, RemoteRelayPayloadTypes.Command, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(parsed.RequestId) ||
                    string.IsNullOrWhiteSpace(parsed.CommandType) ||
                    !string.Equals(parsed.DeviceId, expectedDeviceId, StringComparison.Ordinal))
                {
                    return false;
                }

                command = parsed;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
