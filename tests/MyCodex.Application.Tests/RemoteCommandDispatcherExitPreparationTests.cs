using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class RemoteCommandDispatcherTests
    {
        [Test]
        public async Task ExitPreparationBlocksAdvancedWriteAndResumesWithoutChangingTransport()
        {
            var host = new FakeHostSettingsOperator { Snapshot = CreateHostSettingsSnapshot() };
            var dispatcher = new RemoteCommandDispatcher(new FakeCodingRemoteService(), null,
                new RemoteWriteCoordinator(), null, null, host);
            Assert.That(dispatcher.TryPauseForExit(), Is.True);

            var rejected = await dispatcher.DispatchAsync(CreateOfficialSettingsCommand(), CancellationToken.None);
            Assert.That(rejected.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(host.LastRequest, Is.Null);

            dispatcher.ResumeAfterCancelledExit();
            var result = await dispatcher.DispatchAsync(CreateOfficialSettingsCommand(), CancellationToken.None);
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.HostSettingsUpdateResult));
            Assert.That(host.LastRequest.Official, Is.Not.Null);
        }

        [Test]
        public async Task InFlightRequestMustFinishBeforeExitCanFreezeNewRequests()
        {
            var host = new PendingSettingsOperator();
            var dispatcher = new RemoteCommandDispatcher(new FakeCodingRemoteService(), null,
                new RemoteWriteCoordinator(), null, null, host);
            var pending = dispatcher.DispatchAsync(new RemoteRelayCommand
            { CommandType = RemoteCommandTypes.HostSettingsGet, RequestId = "pending", DeviceId = "phone" }, CancellationToken.None);

            Assert.That(dispatcher.TryPauseForExit(), Is.False);
            host.Completion.SetResult(CreateHostSettingsSnapshot());
            Assert.That((await pending).EventType, Is.EqualTo(RemoteEventTypes.HostSettingsResult));
            Assert.That(dispatcher.TryPauseForExit(), Is.True);
        }

        private sealed class PendingSettingsOperator : IHostSettingsOperator
        {
            internal readonly TaskCompletionSource<HostSettingsSnapshot> Completion = new TaskCompletionSource<HostSettingsSnapshot>();
            public Task<HostSettingsSnapshot> GetAsync(CancellationToken cancellationToken) => Completion.Task;
            public Task<HostSettingsUpdateResult> UpdateAsync(HostSettingsUpdateRequest request, CancellationToken cancellationToken)
                => throw new System.NotSupportedException();
        }
    }
}
