using System.Collections.Generic;
using MyCodex.Application.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteRelayPayloadJsonSerializerTests
    {
        [Test]
        public void SerializeCommand_RoundTripsCommandEnvelopeAndPayloadObject()
        {
            var command = new RemoteRelayCommand
            {
                RequestId = "request-1",
                CommandType = RemoteCommandTypes.CodingWorkspacesList,
                DeviceId = "device-a",
                Payload = new Dictionary<string, object>
                {
                    { "limit", 50 },
                    { "cursor", "older" }
                }
            };

            var json = RemoteRelayPayloadJsonSerializer.SerializeCommand(command);
            var roundTrip = RemoteRelayPayloadJsonSerializer.DeserializeCommand(json);

            Assert.That(json, Does.Contain("\"schemaVersion\":1"));
            Assert.That(json, Does.Contain("\"payloadType\":\"remote.command\""));
            Assert.That(json, Does.Contain("\"commandType\":\"coding.workspaces.list\""));
            Assert.That(json, Does.Contain("\"payload\":{\"limit\":50,\"cursor\":\"older\"}"));
            Assert.That(roundTrip.SchemaVersion, Is.EqualTo(1));
            Assert.That(roundTrip.PayloadType, Is.EqualTo(RemoteRelayPayloadTypes.Command));
            Assert.That(roundTrip.RequestId, Is.EqualTo("request-1"));
            Assert.That(roundTrip.CommandType, Is.EqualTo(RemoteCommandTypes.CodingWorkspacesList));
            Assert.That(roundTrip.DeviceId, Is.EqualTo("device-a"));
            Assert.That(roundTrip.Payload["cursor"], Is.EqualTo("older"));
            Assert.That(roundTrip.Payload["limit"], Is.EqualTo(50));
        }

        [Test]
        public void SerializeEvent_RoundTripsCorrelatedEventPayload()
        {
            var remoteEvent = new RemoteRelayEvent
            {
                RequestId = "request-1",
                OperationId = "operation-1",
                EventType = RemoteEventTypes.CodingMessagesListResult,
                Payload = new Dictionary<string, object>
                {
                    { "sessionId", "session-a" },
                    { "nextCursor", "cursor-1" }
                }
            };

            var json = RemoteRelayPayloadJsonSerializer.SerializeEvent(remoteEvent);
            var roundTrip = RemoteRelayPayloadJsonSerializer.DeserializeEvent(json);

            Assert.That(json, Does.Contain("\"schemaVersion\":1"));
            Assert.That(json, Does.Contain("\"payloadType\":\"remote.event\""));
            Assert.That(roundTrip.SchemaVersion, Is.EqualTo(1));
            Assert.That(roundTrip.PayloadType, Is.EqualTo(RemoteRelayPayloadTypes.Event));
            Assert.That(roundTrip.RequestId, Is.EqualTo("request-1"));
            Assert.That(roundTrip.OperationId, Is.EqualTo("operation-1"));
            Assert.That(roundTrip.EventType, Is.EqualTo(RemoteEventTypes.CodingMessagesListResult));
            Assert.That(roundTrip.Payload["sessionId"], Is.EqualTo("session-a"));
            Assert.That(roundTrip.Payload["nextCursor"], Is.EqualTo("cursor-1"));
        }

        [Test]
        public void SerializeResponse_UsesTheSecureRpcResponsePayloadContract()
        {
            var response = new RemoteRelayEvent
            {
                RequestId = "request-1",
                OperationId = "operation-1",
                EventType = RemoteEventTypes.CodingMessagesListResult,
                Payload = new Dictionary<string, object> { { "ok", true } }
            };

            var json = RemoteRelayPayloadJsonSerializer.SerializeResponse(response);

            Assert.That(json, Does.Contain("\"payloadType\":\"remote.response\""));
            Assert.That(json, Does.Contain("\"requestId\":\"request-1\""));
            Assert.That(json, Does.Contain("\"eventType\":\"coding.messages.list.result\""));
            Assert.That(response.PayloadType, Is.EqualTo(RemoteRelayPayloadTypes.Event));
        }

        [Test]
        public void DeserializeCommand_AcceptsLegacyAndroidDraftFields()
        {
            var json = "{\"type\":\"remote.command\",\"commandId\":\"cmd-a\",\"name\":\"coding.sessions.list\",\"arguments\":{\"workspaceId\":\"ws-a\"}}";

            var command = RemoteRelayPayloadJsonSerializer.DeserializeCommand(json);

            Assert.That(command.SchemaVersion, Is.EqualTo(1));
            Assert.That(command.PayloadType, Is.EqualTo(RemoteRelayPayloadTypes.Command));
            Assert.That(command.RequestId, Is.EqualTo("cmd-a"));
            Assert.That(command.CommandType, Is.EqualTo(RemoteCommandTypes.CodingSessionsList));
            Assert.That(command.Payload["workspaceId"], Is.EqualTo("ws-a"));
        }

        [Test]
        public void ToolInputCommandAndEventConstants_AreStableWireValues()
        {
            Assert.That(RemoteCommandTypes.ToolInputRespond, Is.EqualTo("tool.input.respond"));
            Assert.That(RemoteCommandTypes.ToolInputCancel, Is.EqualTo("tool.input.cancel"));
            Assert.That(RemoteEventTypes.ToolInputRequested, Is.EqualTo("tool.input.requested"));
            Assert.That(RemoteEventTypes.ToolInputCompleted, Is.EqualTo("tool.input.completed"));
            Assert.That(RemoteEventTypes.ToolInputResponseResult, Is.EqualTo("tool.input.response.result"));
        }

        [Test]
        public void PlanExecutionCommandAndEventConstants_AreStableWireValues()
        {
            Assert.That(RemoteCommandTypes.PlanExecutionRespond, Is.EqualTo("plan.execution.respond"));
            Assert.That(RemoteEventTypes.PlanExecutionRequested, Is.EqualTo("plan.execution.requested"));
            Assert.That(RemoteEventTypes.PlanExecutionCompleted, Is.EqualTo("plan.execution.completed"));
            Assert.That(RemoteEventTypes.PlanExecutionResponseResult, Is.EqualTo("plan.execution.response.result"));
        }

        [Test]
        public void CodingAttachmentCatalogCommandAndEventConstants_AreStableWireValues()
        {
            Assert.That(RemoteCommandTypes.CodingAttachmentsPluginsList, Is.EqualTo("coding.attachments.plugins.list"));
            Assert.That(RemoteCommandTypes.CodingAttachmentsSkillsList, Is.EqualTo("coding.attachments.skills.list"));
            Assert.That(RemoteEventTypes.CodingAttachmentsPluginsListResult, Is.EqualTo("coding.attachments.plugins.list.result"));
            Assert.That(RemoteEventTypes.CodingAttachmentsSkillsListResult, Is.EqualTo("coding.attachments.skills.list.result"));
        }
    }
}
