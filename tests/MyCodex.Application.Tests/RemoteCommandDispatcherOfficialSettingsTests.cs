using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class RemoteCommandDispatcherTests
    {
        [Test]
        public async Task DispatchAsync_HostOfficialSettingsGet_MapsCurrentContract()
        {
            var host = new FakeHostSettingsOperator { Snapshot = CreateHostSettingsSnapshot() };
            host.Snapshot.Official = new OfficialCodexSettingsSelection { ModelVerbosity = "medium" };
            var dispatcher = new RemoteCommandDispatcher(new FakeCodingRemoteService(), null,
                new RemoteWriteCoordinator(), null, null, host);

            var result = await dispatcher.DispatchAsync(new RemoteRelayCommand
            {
                RequestId = "get-official", DeviceId = "phone", CommandType = RemoteCommandTypes.HostSettingsGet
            }, CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.HostSettingsResult));
            Assert.That(result.Payload.ContainsKey("official"), Is.True);
            var official = (IDictionary<string, object>)result.Payload["official"];
            Assert.That(official["modelVerbosity"], Is.EqualTo("medium"));
            Assert.That(official["viewImageToolEnabled"], Is.Null);
            Assert.That(official["projectDocMaxBytes"], Is.Null);
        }

        [Test]
        public async Task DispatchAsync_HostOfficialSettingsUpdate_ParsesExplicitValuesAndNulls()
        {
            var host = new FakeHostSettingsOperator { Snapshot = CreateHostSettingsSnapshot() };
            var dispatcher = new RemoteCommandDispatcher(new FakeCodingRemoteService(), null,
                new RemoteWriteCoordinator(), null, null, host);
            var command = CreateOfficialSettingsCommand();

            var result = await dispatcher.DispatchAsync(command, CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.HostSettingsUpdateResult));
            Assert.That(host.LastRequest.Official, Is.Not.Null);
            Assert.That(host.LastRequest.Official.ModelVerbosity, Is.EqualTo("medium"));
            Assert.That(host.LastRequest.Official.ViewImageToolEnabled, Is.True);
            Assert.That(host.LastRequest.Official.ToolOutputTokenLimit, Is.EqualTo(16384));
            Assert.That(host.LastRequest.Official.ServiceTier, Is.Null);
        }

        [TestCase("viewImageToolEnabled", "true")]
        [TestCase("toolOutputTokenLimit", 12.5)]
        [TestCase("modelVerbosity", 123)]
        public async Task DispatchAsync_HostOfficialSettingsUpdate_RejectsWrongTypes(string key, object value)
        {
            var host = new FakeHostSettingsOperator { Snapshot = CreateHostSettingsSnapshot() };
            var dispatcher = new RemoteCommandDispatcher(new FakeCodingRemoteService(), null,
                new RemoteWriteCoordinator(), null, null, host);
            var command = CreateOfficialSettingsCommand();
            ((IDictionary<string, object>)command.Payload["official"])[key] = value;

            var result = await dispatcher.DispatchAsync(command, CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.InvalidCommand));
            Assert.That(host.LastRequest, Is.Null);
        }

        [Test]
        public async Task DispatchAsync_HostOfficialSettingsUpdate_RejectsMissingFieldAndReleasesLock()
        {
            var host = new FakeHostSettingsOperator { Snapshot = CreateHostSettingsSnapshot() };
            var coordinator = new RemoteWriteCoordinator();
            var dispatcher = new RemoteCommandDispatcher(new FakeCodingRemoteService(), null, coordinator, null, null, host);
            var command = CreateOfficialSettingsCommand();
            ((IDictionary<string, object>)command.Payload["official"]).Remove("serviceTier");

            var result = await dispatcher.DispatchAsync(command, CancellationToken.None);

            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.InvalidCommand));
            Assert.That(host.LastRequest, Is.Null);
            Assert.That(coordinator.Acquire("other-phone", "next", System.DateTime.UtcNow).IsGranted, Is.True);
        }

        private static RemoteRelayCommand CreateOfficialSettingsCommand()
        {
            var command = new RemoteRelayCommand
            {
                RequestId = "set-official", DeviceId = "phone", CommandType = RemoteCommandTypes.HostSettingsUpdate
            };
            command.Payload["official"] = new Dictionary<string, object>
            {
                { "modelReasoningSummary", null }, { "modelVerbosity", "medium" },
                { "serviceTier", null }, { "webSearchMode", null }, { "webSearchContextSize", null },
                { "viewImageToolEnabled", true }, { "toolOutputTokenLimit", 16384L }, { "projectDocMaxBytes", null }
            };
            return command;
        }
    }
}
