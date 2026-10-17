using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Instances;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetExitAllCoordinatorTests
    {
        private sealed class Scope
        {
            internal readonly TargetInstanceRecord Local = Record("windows", 1);
            internal readonly TargetInstanceRecord Remote = Record("wsl-test", 2);
            internal readonly List<TargetInstanceReply> Running = new List<TargetInstanceReply>();
            internal readonly List<string> Requests = new List<string>();
            internal readonly HashSet<string> Alive = new HashSet<string>();
            internal Func<TargetInstanceRecord, TargetInstanceRequest, TargetInstanceReply> OnSend;

            internal Scope()
            {
                Add(Local);
                Add(Remote);
            }

            internal void Add(TargetInstanceRecord record)
            {
                Running.Add(new TargetInstanceReply { Success = true, Instance = record, Status = new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running } });
                Alive.Add(record.InstanceId);
            }

            internal TargetExitAllCoordinator Coordinator(bool installer = false)
            {
                return new TargetExitAllCoordinator(installer ? null : Local, () => Running.ToArray(), (record, request) =>
                {
                    Requests.Add(request.Operation + ":" + record.TargetId);
                    var reply = OnSend?.Invoke(record, request) ?? new TargetInstanceReply
                    {
                        Success = true, Instance = record, ExitPreparation = TargetExitPreparation.Ready
                    };
                    return Task.FromResult(reply);
                }, record => Alive.Contains(record.InstanceId));
            }
        }

        [Test]
        public async Task InstallerPreparesEveryTargetBeforeCommittingBothEditions()
        {
            var scope = new Scope();
            scope.Remote.ChannelId = "Dev";
            var coordinator = scope.Coordinator(installer: true);
            coordinator.CaptureScope();
            await coordinator.PrepareAsync(CancellationToken.None);
            Assert.That(scope.Requests, Is.EqualTo(new[] { "PrepareExit:windows", "PrepareExit:wsl-test" }));
            await coordinator.CommitOtherTargetsAsync();
            Assert.That(scope.Requests.Skip(4), Is.EqualTo(new[] { "CommitExit:windows", "CommitExit:wsl-test" }));
            // Removing discovery records is not proof that the processes have exited.
            scope.Running.Clear();
            Assert.That(coordinator.GetRemainingOtherTargets().Count, Is.EqualTo(2));
            scope.Alive.Clear();
            Assert.That(await coordinator.WaitForOtherTargetsAsync(CancellationToken.None), Is.Empty);
        }

        [Test]
        public void InstallerRejectedSaveLeavesAllTargetsRunning()
        {
            var scope = new Scope();
            scope.OnSend = (record, request) => request.Operation == TargetInstanceOperation.PrepareExit && record == scope.Remote
                ? new TargetInstanceReply { Instance = record, ExitPreparation = TargetExitPreparation.Rejected } : null;
            var coordinator = scope.Coordinator(installer: true);
            coordinator.CaptureScope();
            Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PrepareAsync(CancellationToken.None));
            Assert.That(scope.Requests.Where(item => item.StartsWith("AbortExit:", StringComparison.Ordinal)).Count(), Is.EqualTo(2));
            Assert.That(scope.Requests.Any(item => item.StartsWith("CommitExit:", StringComparison.Ordinal)), Is.False);
        }

        [TestCase("C:\\app\\MyCodex.exe", "C:\\app\\MyCodex.Dev.exe", true, true)]
        [TestCase("C:\\app\\MyCodex.exe", "C:\\app\\MyCodex.Dev.exe", false, false)]
        [TestCase("C:\\app\\MyCodex.exe", "C:\\other\\MyCodex.Dev.exe", true, false)]
        [TestCase("C:\\app\\helper.exe", "C:\\app\\MyCodex.exe", true, false)]
        [TestCase("C:\\app\\MyCodex.exe", "C:\\app\\helper.exe", true, false)]
        public void InstallationExitTrustsOnlyClientEditionsInSameDirectory(string caller, string owner, bool maintenance, bool expected)
        {
            Assert.That(TargetInstanceSecurity.IsSameInstallationPath(caller, owner, maintenance), Is.EqualTo(expected));
        }

        [Test]
        public async Task AllTargetsPrepareBeforeOtherTargetsCommitAndLocalRemainsForLast()
        {
            var scope = new Scope();
            var coordinator = scope.Coordinator();
            coordinator.CaptureScope();
            await coordinator.PrepareAsync(CancellationToken.None);
            Assert.That(scope.Requests, Is.EqualTo(new[] { "PrepareExit:windows", "PrepareExit:wsl-test" }));
            await coordinator.CommitOtherTargetsAsync();
            Assert.That(scope.Requests.Last(), Is.EqualTo("CommitExit:wsl-test"));
            Assert.That(scope.Requests, Does.Not.Contain("CommitExit:windows"));
        }

        [Test]
        public void OneRejectedSaveAbortsEveryAttemptedTargetAndStopsNone()
        {
            var scope = new Scope();
            scope.OnSend = (record, request) => request.Operation == TargetInstanceOperation.PrepareExit && record == scope.Remote
                ? new TargetInstanceReply { Instance = record, Error = "Save cancelled", ExitPreparation = TargetExitPreparation.Rejected } : null;
            var coordinator = scope.Coordinator();
            coordinator.CaptureScope();
            Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PrepareAsync(CancellationToken.None));
            Assert.That(scope.Requests.Where(item => item.StartsWith("AbortExit:", StringComparison.Ordinal)),
                Is.EquivalentTo(new[] { "AbortExit:windows", "AbortExit:wsl-test" }));
            Assert.That(scope.Requests.Any(item => item.StartsWith("CommitExit:", StringComparison.Ordinal)), Is.False);
            Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CommitOtherTargetsAsync());
        }

        [Test]
        public async Task PendingPreparationIsPolledWithoutTreatingAcceptanceAsReady()
        {
            var scope = new Scope();
            var pending = 0;
            scope.OnSend = (record, request) => record == scope.Remote && request.Operation == TargetInstanceOperation.PrepareExit && pending++ == 0
                ? new TargetInstanceReply { Success = true, Instance = record, ExitPreparation = TargetExitPreparation.Pending } : null;
            var coordinator = scope.Coordinator();
            coordinator.CaptureScope();
            await coordinator.PrepareAsync(CancellationToken.None);
            Assert.That(scope.Requests.Count(item => item == "PrepareExit:wsl-test"), Is.EqualTo(2));
            Assert.That(scope.Requests.Any(item => item.StartsWith("CommitExit:", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void CancellationWhilePendingAbortsLocalAndPendingOwner()
        {
            var scope = new Scope();
            using (var cancellation = new CancellationTokenSource())
            {
                scope.OnSend = (record, request) =>
                {
                    if (record != scope.Remote || request.Operation != TargetInstanceOperation.PrepareExit) return null;
                    cancellation.Cancel();
                    return new TargetInstanceReply { Success = true, Instance = record, ExitPreparation = TargetExitPreparation.Pending };
                };
                var coordinator = scope.Coordinator();
                coordinator.CaptureScope();
                Assert.ThrowsAsync<TaskCanceledException>(() => coordinator.PrepareAsync(cancellation.Token));
                Assert.That(scope.Requests.Skip(scope.Requests.Count - 2), Is.EqualTo(new[] { "AbortExit:windows", "AbortExit:wsl-test" }));
                Assert.That(scope.Requests.Any(item => item.StartsWith("CommitExit:", StringComparison.Ordinal)), Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ScopeChangeAfterPreparationNeverClosesNewOrReplacedOwner(bool replacement)
        {
            var scope = new Scope();
            var coordinator = scope.Coordinator();
            coordinator.CaptureScope();
            await coordinator.PrepareAsync(CancellationToken.None);
            if (replacement) scope.Running.RemoveAll(item => item.Instance == scope.Remote);
            scope.Add(Record(replacement ? scope.Remote.TargetId : "ssh-added", 3));
            Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CommitOtherTargetsAsync());
            Assert.That(scope.Requests.Any(item => item.StartsWith("CommitExit:", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void DiscoveryOrPreparedResponseWithUnknownIdentityCannotAuthorizeExit()
        {
            var scope = new Scope();
            scope.Running.Add(TargetInstanceReply.Failure("Unverified owner"));
            Assert.Throws<InvalidOperationException>(() => scope.Coordinator().CaptureScope());
            scope.Running.RemoveAt(scope.Running.Count - 1);
            scope.OnSend = (record, request) => request.Operation == TargetInstanceOperation.PrepareExit
                ? new TargetInstanceReply { Success = true, ExitPreparation = TargetExitPreparation.Ready } : null;
            var coordinator = scope.Coordinator();
            coordinator.CaptureScope();
            Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PrepareAsync(CancellationToken.None));
            Assert.That(scope.Requests.Any(item => item.StartsWith("CommitExit:", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void DeletedRegistryRecordDoesNotProveTheOwnerProcessExited()
        {
            var scope = new Scope();
            var coordinator = scope.Coordinator();
            coordinator.CaptureScope();
            scope.Running.RemoveAll(item => item.Instance == scope.Remote);
            var remaining = coordinator.GetRemainingOtherTargets();
            Assert.That(remaining.Single().Instance.InstanceId, Is.EqualTo(scope.Remote.InstanceId));
            Assert.That(remaining.Single().Success, Is.False);
            scope.Alive.Remove(scope.Remote.InstanceId);
            Assert.That(coordinator.GetRemainingOtherTargets(), Is.Empty);
        }

        [Test]
        public async Task LostCommitAcknowledgementCanCompleteOnlyAfterExactOwnerActuallyExited()
        {
            var scope = new Scope();
            var coordinator = scope.Coordinator();
            coordinator.CaptureScope();
            await coordinator.PrepareAsync(CancellationToken.None);
            scope.OnSend = (record, request) =>
            {
                if (request.Operation != TargetInstanceOperation.CommitExit) return null;
                scope.Running.RemoveAll(item => item.Instance == record);
                scope.Alive.Remove(record.InstanceId);
                return new TargetInstanceReply { Instance = record, Error = "Pipe closed while the owner exited." };
            };
            await coordinator.CommitOtherTargetsAsync();
            Assert.That(coordinator.GetRemainingOtherTargets(), Is.Empty);
        }

        [Test]
        public async Task ExpiredParticipantAtCommitPreflightStopsNoOtherTarget()
        {
            var scope = new Scope();
            var coordinator = scope.Coordinator();
            coordinator.CaptureScope();
            await coordinator.PrepareAsync(CancellationToken.None);
            scope.OnSend = (record, request) => record == scope.Remote && request.Operation == TargetInstanceOperation.PrepareExit
                ? new TargetInstanceReply { Instance = record, Error = "Lease expired", ExitPreparation = TargetExitPreparation.Rejected } : null;
            Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CommitOtherTargetsAsync());
            Assert.That(scope.Requests.Any(item => item.StartsWith("CommitExit:", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public async Task FailedCancellationIsReportedRatherThanClaimingEveryOwnerResumed()
        {
            var scope = new Scope();
            var coordinator = scope.Coordinator();
            coordinator.CaptureScope();
            await coordinator.PrepareAsync(CancellationToken.None);
            scope.OnSend = (record, request) => record == scope.Remote && request.Operation == TargetInstanceOperation.AbortExit
                ? TargetInstanceReply.Failure("Connection lost") : null;
            await coordinator.AbortAsync();
            Assert.That(coordinator.AbortFailures, Is.EqualTo(new[] { scope.Remote }));
        }

        private static TargetInstanceRecord Record(string target, int processId)
        {
            return new TargetInstanceRecord
            {
                TargetId = target, ChannelId = "test", InstanceId = Guid.NewGuid().ToString("N"),
                ProcessId = processId, StartTimeUtcTicks = processId, ExecutablePath = "C:\\test\\MyCodex.exe", UserSid = "test-user"
            };
        }
    }
}
