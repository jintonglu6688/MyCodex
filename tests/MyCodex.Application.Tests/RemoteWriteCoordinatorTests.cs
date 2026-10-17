using System;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteWriteCoordinatorTests
    {
        [Test]
        public void Acquire_DeniesSecondDeviceUntilHolderReleases()
        {
            var now = new DateTime(2026, 6, 7, 8, 0, 0, DateTimeKind.Utc);
            var coordinator = new RemoteWriteCoordinator(TimeSpan.FromSeconds(30));

            var first = coordinator.Acquire("device-a", "operation-a", now);
            var second = coordinator.Acquire("device-b", "operation-b", now.AddSeconds(1));
            var wrongRelease = coordinator.Release("device-b");
            var rightRelease = coordinator.Release("device-a");
            var third = coordinator.Acquire("device-b", "operation-b", now.AddSeconds(2));

            Assert.That(first.IsGranted, Is.True);
            Assert.That(first.HolderDeviceId, Is.EqualTo("device-a"));
            Assert.That(second.IsGranted, Is.False);
            Assert.That(second.HolderDeviceId, Is.EqualTo("device-a"));
            Assert.That(wrongRelease, Is.False);
            Assert.That(rightRelease, Is.True);
            Assert.That(third.IsGranted, Is.True);
            Assert.That(third.HolderDeviceId, Is.EqualTo("device-b"));
        }

        [Test]
        public void Acquire_ReplacesExpiredHolder()
        {
            var now = new DateTime(2026, 6, 7, 8, 0, 0, DateTimeKind.Utc);
            var coordinator = new RemoteWriteCoordinator(TimeSpan.FromSeconds(30));

            coordinator.Acquire("device-a", "operation-a", now);
            var result = coordinator.Acquire("device-b", "operation-b", now.AddSeconds(31));

            Assert.That(result.IsGranted, Is.True);
            Assert.That(result.HolderDeviceId, Is.EqualTo("device-b"));
        }

        [Test]
        public void Heartbeat_RenewsOnlyCurrentHolder()
        {
            var now = new DateTime(2026, 6, 7, 8, 0, 0, DateTimeKind.Utc);
            var coordinator = new RemoteWriteCoordinator(TimeSpan.FromSeconds(30));

            coordinator.Acquire("device-a", "operation-a", now);
            var rejected = coordinator.Heartbeat("device-b", now.AddSeconds(5));
            var renewed = coordinator.Heartbeat("device-a", now.AddSeconds(10));
            var deniedBeforeRenewedExpiry = coordinator.Acquire("device-b", "operation-b", now.AddSeconds(35));
            var grantedAfterRenewedExpiry = coordinator.Acquire("device-b", "operation-b", now.AddSeconds(41));

            Assert.That(rejected, Is.False);
            Assert.That(renewed, Is.True);
            Assert.That(deniedBeforeRenewedExpiry.IsGranted, Is.False);
            Assert.That(grantedAfterRenewedExpiry.IsGranted, Is.True);
        }

        [Test]
        public async Task BeginSessionOperation_DistinguishesDuplicateReplayAndConflict()
        {
            var now = new DateTime(2026, 6, 7, 8, 0, 0, DateTimeKind.Utc);
            var coordinator = new RemoteWriteCoordinator(TimeSpan.FromSeconds(30));
            coordinator.Acquire("device-a", "operation-a", now);

            var first = coordinator.BeginSessionOperation("device-a", "session-a", "operation-a");
            var duplicate = coordinator.BeginSessionOperation("device-a", "session-a", "operation-a");
            var conflict = coordinator.BeginSessionOperation("device-a", "session-a", "operation-b");
            var completedEvent = new RemoteRelayEvent { RequestId = "request-a" };
            first.Complete(completedEvent);
            var replay = coordinator.BeginSessionOperation("device-a", "session-a", "operation-a");

            Assert.That(first.State, Is.EqualTo(RemoteSessionOperationState.First));
            Assert.That(duplicate.State, Is.EqualTo(RemoteSessionOperationState.InFlight));
            Assert.That(conflict.State, Is.EqualTo(RemoteSessionOperationState.Conflict));
            Assert.That(replay.State, Is.EqualTo(RemoteSessionOperationState.Completed));
            Assert.That(await duplicate.Completion, Is.SameAs(completedEvent));
            Assert.That(await replay.Completion, Is.SameAs(completedEvent));
        }

        [Test]
        public void BeginSessionOperation_DoesNotReplayCompletedResultToAnotherDevice()
        {
            var coordinator = new RemoteWriteCoordinator();
            coordinator.Acquire("device-a", "operation-a", DateTime.UtcNow);
            var first = coordinator.BeginSessionOperation("device-a", "session-a", "operation-a");
            first.Complete(new RemoteRelayEvent());
            coordinator.Release("device-a");
            coordinator.Acquire("device-b", "operation-a", DateTime.UtcNow);

            var replay = coordinator.BeginSessionOperation("device-b", "session-a", "operation-a");

            Assert.That(replay.State, Is.EqualTo(RemoteSessionOperationState.Conflict));
        }

        [Test]
        public async Task BeginSessionOperation_EvictsOldestCompletedResultAtCapacity()
        {
            var coordinator = new RemoteWriteCoordinator();
            for (var index = 0; index <= 256; index++)
            {
                var operationId = "operation-" + index;
                coordinator.Acquire("device-a", operationId, DateTime.UtcNow);
                var lease = coordinator.BeginSessionOperation("device-a", "session-a", operationId);
                lease.Complete(new RemoteRelayEvent { OperationId = operationId });
                coordinator.Release("device-a");
            }

            coordinator.Acquire("device-a", "operation-256", DateTime.UtcNow);
            var recent = coordinator.BeginSessionOperation("device-a", "session-a", "operation-256");
            coordinator.Release("device-a");
            coordinator.Acquire("device-a", "operation-0", DateTime.UtcNow);
            var oldest = coordinator.BeginSessionOperation("device-a", "session-a", "operation-0");

            Assert.That(recent.State, Is.EqualTo(RemoteSessionOperationState.Completed));
            Assert.That((await recent.Completion).OperationId, Is.EqualTo("operation-256"));
            Assert.That(oldest.State, Is.EqualTo(RemoteSessionOperationState.First));
        }

        [Test]
        public void Release_DoesNotExposeAnotherActiveSessionFromSameDevice()
        {
            var coordinator = new RemoteWriteCoordinator();
            coordinator.Acquire("device-a", "operation-a", DateTime.UtcNow);
            var first = coordinator.BeginSessionOperation("device-a", "session-a", "operation-a");
            coordinator.Acquire("device-a", "operation-b", DateTime.UtcNow);
            coordinator.BeginSessionOperation("device-a", "session-b", "operation-b");
            first.Complete(new RemoteRelayEvent());

            var released = coordinator.Release("device-a");
            var otherDevice = coordinator.Acquire("device-b", "operation-c", DateTime.UtcNow);

            Assert.That(released, Is.False);
            Assert.That(otherDevice.IsGranted, Is.False);
        }
    }
}
