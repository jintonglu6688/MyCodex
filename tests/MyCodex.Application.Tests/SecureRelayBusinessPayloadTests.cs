using System;
using MyCodex.Application.RemoteRelay;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SecureRelayBusinessPayloadTests
    {
        [Test]
        public void CommandPayload_RequiresAuthenticatedRequiredFields()
        {
            RemoteRelayCommand command;
            Assert.That(
                RemoteRelayResponseEnvelopePlanner.TryDeserializeRemoteCommand(
                    "{\"payloadType\":\"remote.command\",\"requestId\":\"r1\",\"deviceId\":\"d1\",\"commandType\":\"workspace.list\",\"payload\":{}}",
                    "d1",
                    out command),
                Is.True);
            Assert.That(command.RequestId, Is.EqualTo("r1"));
            Assert.That(
                RemoteRelayResponseEnvelopePlanner.TryDeserializeRemoteCommand(
                    "{\"requestId\":\"r1\",\"deviceId\":\"d1\",\"commandType\":\"workspace.list\"}",
                    "d1",
                    out command),
                Is.False);
            Assert.That(
                RemoteRelayResponseEnvelopePlanner.TryDeserializeRemoteCommand(
                    "{\"payloadType\":\"remote.command\",\"requestId\":\"r1\",\"deviceId\":\"other\",\"commandType\":\"workspace.list\"}",
                    "d1",
                    out command),
                Is.False);
        }

        [Test]
        public void CommandPayload_AcceptsAuthenticatedPayloadBeyondTheDefaultSerializerLimit()
        {
            var largeText = new string('x', 2 * 1024 * 1024 + 1);
            var json = "{\"payloadType\":\"remote.command\",\"requestId\":\"r1\",\"deviceId\":\"d1\",\"commandType\":\"workspace.list\",\"payload\":{\"text\":\"" + largeText + "\"}}";

            RemoteRelayCommand command;
            var accepted = RemoteRelayResponseEnvelopePlanner.TryDeserializeRemoteCommand(json, "d1", out command);

            Assert.That(accepted, Is.True);
            Assert.That(command.Payload["text"], Is.EqualTo(largeText));
        }

        [Test]
        public void OuterFrame_AcceptsAnEnvelopeBeyondTheDefaultSerializerLimit()
        {
            var ciphertext = new string('a', 2 * 1024 * 1024 + 1);
            var json = "{\"protocolVersion\":1,\"frameType\":\"session.envelope\",\"sessionId\":\"s\",\"tenantId\":\"t\",\"hostId\":\"h\",\"deviceId\":\"d\",\"direction\":\"android_to_windows\",\"kind\":\"rpc.request\",\"messageId\":\"m\",\"sequence\":1,\"createdAt\":1,\"payloadEncoding\":\"encrypted-json\",\"nonce\":\"n\",\"ciphertext\":\"" + ciphertext + "\"}";

            Assert.That(new SecureRelayFrameCodec().Deserialize(json), Is.TypeOf<SecureRelayEnvelope>());
        }

        [Test]
        public void OuterFrame_RejectsAFrameBeyondTheProtocolWireLimit()
        {
            Assert.That(
                () => new SecureRelayFrameCodec().Deserialize(new string('x', SecureRelayChannel.MaxWireFrameBytes + 1)),
                Throws.TypeOf<FormatException>());
        }
    }
}
