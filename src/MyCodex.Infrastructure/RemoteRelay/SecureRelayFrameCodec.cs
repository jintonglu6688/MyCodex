using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Web.Script.Serialization;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Infrastructure.RemoteRelay
{
    // This codec is deliberately limited to the four protocol frames.  Business JSON is encrypted.
    internal sealed class SecureRelayFrameCodec
    {
        private static readonly string[] HelloFields =
        {
            "protocolVersion", "frameType", "sessionId", "tenantId", "hostId", "deviceId",
            "direction", "bindingVersion", "keyVersion", "ephemeralPublicKey", "nonce", "createdAt", "signature"
        };
        private static readonly string[] ConfirmationFields =
        {
            "protocolVersion", "frameType", "sessionId", "tenantId", "hostId", "deviceId", "direction", "senderRole", "confirmation"
        };
        private static readonly string[] EnvelopeFields =
        {
            "protocolVersion", "frameType", "sessionId", "tenantId", "hostId", "deviceId", "direction", "kind", "messageId",
            "sequence", "createdAt", "payloadEncoding", "nonce", "ciphertext"
        };

        public object Deserialize(string text)
        {
            if (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) > SecureRelayChannel.MaxWireFrameBytes)
            {
                throw new FormatException("The Relay frame is invalid.");
            }

            var fields = DeserializeObject(text);
            var frameType = RequireString(fields, "frameType");
            if (string.Equals(frameType, SecureRelayFrameTypes.ClientHello, StringComparison.Ordinal) ||
                string.Equals(frameType, SecureRelayFrameTypes.ServerHello, StringComparison.Ordinal))
            {
                RequireExactFields(fields, HelloFields);
                return new SessionHello
                {
                    ProtocolVersion = RequireInt64(fields, "protocolVersion"),
                    FrameType = frameType,
                    SessionId = RequireString(fields, "sessionId"),
                    TenantId = RequireString(fields, "tenantId"),
                    HostId = RequireString(fields, "hostId"),
                    DeviceId = RequireString(fields, "deviceId"),
                    Direction = RequireString(fields, "direction"),
                    BindingVersion = RequireInt64(fields, "bindingVersion"),
                    KeyVersion = RequireInt64(fields, "keyVersion"),
                    EphemeralPublicKey = RequireString(fields, "ephemeralPublicKey"),
                    Nonce = RequireString(fields, "nonce"),
                    CreatedAt = RequireInt64(fields, "createdAt"),
                    Signature = RequireString(fields, "signature")
                };
            }

            if (string.Equals(frameType, SecureRelayFrameTypes.Confirmation, StringComparison.Ordinal))
            {
                RequireExactFields(fields, ConfirmationFields);
                return new SessionConfirmationFrame
                {
                    ProtocolVersion = RequireInt64(fields, "protocolVersion"),
                    FrameType = frameType,
                    SessionId = RequireString(fields, "sessionId"),
                    TenantId = RequireString(fields, "tenantId"),
                    HostId = RequireString(fields, "hostId"),
                    DeviceId = RequireString(fields, "deviceId"),
                    Direction = RequireString(fields, "direction"),
                    SenderRole = RequireString(fields, "senderRole"),
                    Confirmation = RequireString(fields, "confirmation")
                };
            }

            if (string.Equals(frameType, SecureRelayFrameTypes.Envelope, StringComparison.Ordinal))
            {
                RequireExactFields(fields, EnvelopeFields);
                return new SecureRelayEnvelope
                {
                    ProtocolVersion = RequireInt64(fields, "protocolVersion"),
                    FrameType = frameType,
                    SessionId = RequireString(fields, "sessionId"),
                    TenantId = RequireString(fields, "tenantId"),
                    HostId = RequireString(fields, "hostId"),
                    DeviceId = RequireString(fields, "deviceId"),
                    Direction = RequireString(fields, "direction"),
                    Kind = RequireString(fields, "kind"),
                    MessageId = RequireString(fields, "messageId"),
                    Sequence = RequireUInt64(fields, "sequence"),
                    CreatedAt = RequireInt64(fields, "createdAt"),
                    PayloadEncoding = RequireString(fields, "payloadEncoding"),
                    Nonce = RequireString(fields, "nonce"),
                    Ciphertext = RequireString(fields, "ciphertext")
                };
            }

            throw new FormatException("The Relay frame type is invalid.");
        }

        public string Serialize(object frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            using (var output = new MemoryStream())
            {
                new DataContractJsonSerializer(frame.GetType()).WriteObject(output, frame);
                var text = Encoding.UTF8.GetString(output.ToArray());
                if (Encoding.UTF8.GetByteCount(text) > SecureRelayChannel.MaxWireFrameBytes)
                {
                    throw new InvalidOperationException("The Relay frame is too large.");
                }

                return text;
            }
        }

        private static IDictionary<string, object> DeserializeObject(string text)
        {
            RejectDuplicateRootFields(text);
            try
            {
                var value = new JavaScriptSerializer
                {
                    MaxJsonLength = SecureRelayChannel.MaxWireFrameBytes
                }.DeserializeObject(text) as IDictionary<string, object>;
                if (value == null)
                {
                    throw new FormatException("The Relay frame must be an object.");
                }

                return value;
            }
            catch (ArgumentException ex)
            {
                throw new FormatException("The Relay frame is invalid.", ex);
            }
        }

        private static void RequireExactFields(IDictionary<string, object> values, IEnumerable<string> expected)
        {
            var set = new HashSet<string>(expected, StringComparer.Ordinal);
            if (values.Count != set.Count)
            {
                throw new FormatException("The Relay frame fields are invalid.");
            }

            foreach (var pair in values)
            {
                if (!set.Contains(pair.Key) || pair.Value == null)
                {
                    throw new FormatException("The Relay frame fields are invalid.");
                }
            }
        }

        private static string RequireString(IDictionary<string, object> values, string name)
        {
            object value;
            if (!values.TryGetValue(name, out value) || !(value is string) || string.IsNullOrWhiteSpace((string)value))
            {
                throw new FormatException("The Relay frame contains an invalid string.");
            }

            return (string)value;
        }

        private static long RequireInt64(IDictionary<string, object> values, string name)
        {
            object value;
            if (!values.TryGetValue(name, out value) || !(value is int) && !(value is long))
            {
                throw new FormatException("The Relay frame contains an invalid integer.");
            }

            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private static ulong RequireUInt64(IDictionary<string, object> values, string name)
        {
            var value = RequireInt64(values, name);
            if (value < 0)
            {
                throw new FormatException("The Relay frame contains an invalid integer.");
            }

            return (ulong)value;
        }

        private static void RejectDuplicateRootFields(string text)
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (character == '"')
                {
                    var end = index + 1;
                    var escape = false;
                    for (; end < text.Length; end++)
                    {
                        if (!escape && text[end] == '"') break;
                        escape = !escape && text[end] == '\\';
                        if (text[end] != '\\') escape = false;
                    }
                    var next = end + 1;
                    while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                    if (depth == 1 && next < text.Length && text[next] == ':')
                    {
                        var key = text.Substring(index + 1, end - index - 1);
                        if (key.IndexOf('\\') >= 0 || !fields.Add(key))
                        {
                            throw new FormatException("The Relay frame contains duplicate fields.");
                        }
                    }
                    inString = true;
                }
                else if (character == '{' || character == '[') depth++;
                else if (character == '}' || character == ']') depth--;
            }
        }
    }
}
