using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using MyCodex.Shared.Helpers;

namespace MyCodex.Application.RemoteRelay
{
    public static class RemoteRelayPayloadJsonSerializer
    {
        public const int MaxBusinessJsonCharacters = 11 * 1024 * 1024;

        public static string SerializeCommand(RemoteRelayCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            command.SchemaVersion = command.SchemaVersion <= 0 ? 1 : command.SchemaVersion;
            command.PayloadType = RemoteRelayPayloadTypes.Command;
            if (command.Payload == null)
            {
                command.Payload = new Dictionary<string, object>();
            }

            return CreateSerializer().Serialize(new Dictionary<string, object>
            {
                { "schemaVersion", command.SchemaVersion },
                { "payloadType", command.PayloadType },
                { "requestId", command.RequestId },
                { "operationId", command.OperationId },
                { "deviceId", command.DeviceId },
                { "commandType", command.CommandType },
                { "payload", command.Payload }
            });
        }

        public static RemoteRelayCommand DeserializeCommand(string json)
        {
            var root = CreateSerializer().DeserializeObject(json ?? string.Empty) as IDictionary<string, object>;
            var command = new RemoteRelayCommand();
            if (root == null)
            {
                return command;
            }

            command.SchemaVersion = GetInt(root, "schemaVersion", 1);
            command.PayloadType = JsonValueReader.GetString(root, "payloadType") ?? JsonValueReader.GetString(root, "type") ?? RemoteRelayPayloadTypes.Command;
            command.RequestId = JsonValueReader.GetString(root, "requestId") ?? JsonValueReader.GetString(root, "commandId");
            command.CommandType = JsonValueReader.GetString(root, "commandType") ?? JsonValueReader.GetString(root, "name");
            command.DeviceId = JsonValueReader.GetString(root, "deviceId");
            command.OperationId = JsonValueReader.GetString(root, "operationId");
            command.Payload = GetDictionary(root, "payload");
            if (command.Payload.Count == 0)
            {
                command.Payload = GetDictionary(root, "arguments");
            }

            if (command.SchemaVersion <= 0)
            {
                command.SchemaVersion = 1;
            }

            if (string.IsNullOrWhiteSpace(command.PayloadType))
            {
                command.PayloadType = RemoteRelayPayloadTypes.Command;
            }

            if (command.Payload == null)
            {
                command.Payload = new Dictionary<string, object>();
            }

            return command;
        }

        public static string SerializeEvent(RemoteRelayEvent remoteEvent)
        {
            if (remoteEvent == null)
            {
                throw new ArgumentNullException(nameof(remoteEvent));
            }

            remoteEvent.SchemaVersion = remoteEvent.SchemaVersion <= 0 ? 1 : remoteEvent.SchemaVersion;
            remoteEvent.PayloadType = RemoteRelayPayloadTypes.Event;
            if (remoteEvent.Payload == null)
            {
                remoteEvent.Payload = new Dictionary<string, object>();
            }

            return CreateSerializer().Serialize(new Dictionary<string, object>
            {
                { "schemaVersion", remoteEvent.SchemaVersion },
                { "payloadType", remoteEvent.PayloadType },
                { "requestId", remoteEvent.RequestId },
                { "operationId", remoteEvent.OperationId },
                { "eventType", remoteEvent.EventType },
                { "payload", remoteEvent.Payload }
            });
        }

        public static string SerializeResponse(RemoteRelayEvent response)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            if (string.IsNullOrWhiteSpace(response.RequestId))
            {
                throw new ArgumentException("A secure Relay response requires a request ID.", nameof(response));
            }

            response.SchemaVersion = response.SchemaVersion <= 0 ? 1 : response.SchemaVersion;
            if (response.Payload == null)
            {
                response.Payload = new Dictionary<string, object>();
            }

            return CreateSerializer().Serialize(new Dictionary<string, object>
            {
                { "schemaVersion", response.SchemaVersion },
                { "payloadType", RemoteRelayPayloadTypes.Response },
                { "requestId", response.RequestId },
                { "operationId", response.OperationId },
                { "eventType", response.EventType },
                { "payload", response.Payload }
            });
        }

        public static RemoteRelayEvent DeserializeEvent(string json)
        {
            var root = CreateSerializer().DeserializeObject(json ?? string.Empty) as IDictionary<string, object>;
            var remoteEvent = new RemoteRelayEvent();
            if (root == null)
            {
                return remoteEvent;
            }

            remoteEvent.SchemaVersion = GetInt(root, "schemaVersion", 1);
            remoteEvent.PayloadType = JsonValueReader.GetString(root, "payloadType") ?? JsonValueReader.GetString(root, "type") ?? RemoteRelayPayloadTypes.Event;
            remoteEvent.EventType = JsonValueReader.GetString(root, "eventType") ?? JsonValueReader.GetString(root, "name");
            remoteEvent.RequestId = JsonValueReader.GetString(root, "requestId") ?? JsonValueReader.GetString(root, "commandId");
            remoteEvent.OperationId = JsonValueReader.GetString(root, "operationId");
            remoteEvent.Payload = GetDictionary(root, "payload");
            if (remoteEvent.Payload.Count == 0)
            {
                remoteEvent.Payload = GetDictionary(root, "data");
            }

            if (remoteEvent.SchemaVersion <= 0)
            {
                remoteEvent.SchemaVersion = 1;
            }

            if (string.IsNullOrWhiteSpace(remoteEvent.PayloadType))
            {
                remoteEvent.PayloadType = RemoteRelayPayloadTypes.Event;
            }

            if (remoteEvent.Payload == null)
            {
                remoteEvent.Payload = new Dictionary<string, object>();
            }

            return remoteEvent;
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaxBusinessJsonCharacters };
        }

        private static int GetInt(IDictionary<string, object> source, string key, int defaultValue)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return defaultValue;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch (FormatException)
            {
                return defaultValue;
            }
            catch (InvalidCastException)
            {
                return defaultValue;
            }
        }

        private static Dictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return new Dictionary<string, object>();
            }

            var dictionary = value as Dictionary<string, object>;
            if (dictionary != null)
            {
                return dictionary;
            }

            var readOnly = value as IDictionary<string, object>;
            return readOnly != null
                ? new Dictionary<string, object>(readOnly)
                : new Dictionary<string, object>();
        }
    }
}
