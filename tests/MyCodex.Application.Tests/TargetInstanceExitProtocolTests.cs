using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Instances;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class TargetInstanceExitProtocolTests
    {
        private string _root;
        private string _channel;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "target-exit-ipc-" + Guid.NewGuid().ToString("N"));
            _channel = "ExitTest-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown() { Directory.Delete(_root, true); }

        [Test]
        public void PendingSaveKeepsPipeResponsiveAndRequiresSamePreparedOperation()
        {
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, "windows", _root))
            {
                var save = new TaskCompletionSource<TargetInstanceReply>();
                var committed = new ManualResetEventSlim();
                var cancelled = 0;
                owner.StartServer(request =>
                {
                    if (request.Operation == TargetInstanceOperation.PrepareExit) return save.Task;
                    if (request.Operation == TargetInstanceOperation.AbortExit) Interlocked.Increment(ref cancelled);
                    return Task.FromResult(new TargetInstanceReply
                    {
                        Success = true,
                        AfterResponse = request.Operation == TargetInstanceOperation.CommitExit ? (Action)(() => committed.Set()) : null
                    });
                });
                owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running });
                var operation = Guid.NewGuid().ToString("N");
                Assert.That(Send(TargetInstanceOperation.PrepareExit, operation).ExitPreparation, Is.EqualTo(TargetExitPreparation.Pending));
                Assert.That(Send(TargetInstanceOperation.Status).Status.IsExitPrepared, Is.True);
                Assert.That(Send(TargetInstanceOperation.CommitExit, operation).Success, Is.False, "An accepted but pending save is not ready.");
                Assert.That(Send(TargetInstanceOperation.AbortExit, Guid.NewGuid().ToString("N")).Success, Is.False);
                Assert.That(cancelled, Is.Zero);
                save.SetResult(new TargetInstanceReply { Success = true });
                Assert.That(SpinWait.SpinUntil(() => Send(TargetInstanceOperation.PrepareExit, operation).ExitPreparation == TargetExitPreparation.Ready, 3000), Is.True);
                var commit = Send(TargetInstanceOperation.CommitExit, operation);
                Assert.That(commit.Success, Is.True, commit.Error);
                Assert.That(committed.Wait(3000), Is.True, "Commit callback follows the reply wire write.");
                Assert.That(Send(TargetInstanceOperation.AbortExit, operation).Success, Is.True);
                Assert.That(Send(TargetInstanceOperation.Status).Status.IsExitPrepared, Is.False);
                Assert.That(cancelled, Is.EqualTo(1));
                committed.Dispose();
            }
        }

        [Test]
        public void ReplacedOwnerIsNeverSentACommitForThePreviousIdentity()
        {
            TargetInstanceRecord expected;
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, "windows", _root)) expected = owner.InstanceRecord;
            using (var replacement = TargetInstanceCoordinator.Acquire(_channel, "windows", _root))
            {
                var received = 0;
                replacement.StartServer(request => { received++; return Task.FromResult(new TargetInstanceReply { Success = true }); });
                replacement.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running });
                var reply = TargetInstanceCoordinator.SendToInstance(expected, _root,
                    new TargetInstanceRequest { Operation = TargetInstanceOperation.CommitExit, OperationId = Guid.NewGuid().ToString("N") });
                Assert.That(reply.Success, Is.False);
                Assert.That(received, Is.Zero);
                Assert.That(reply.Instance.InstanceId, Is.EqualTo(replacement.InstanceRecord.InstanceId));
            }
        }

        [Test]
        public void MissingCoordinatorAutomaticallyCancelsPreparedTarget()
        {
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, "windows", _root))
            using (var cancelled = new ManualResetEventSlim())
            {
                TargetInstanceRequest received = null;
                owner.StartServer(request =>
                {
                    if (request.Operation == TargetInstanceOperation.PrepareExit) received = request;
                    if (request.Operation == TargetInstanceOperation.AbortExit) cancelled.Set();
                    return Task.FromResult(new TargetInstanceReply { Success = true });
                });
                owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running });
                var operation = Guid.NewGuid().ToString("N");
                Send(TargetInstanceOperation.PrepareExit, operation);
                Assert.That(SpinWait.SpinUntil(() => Send(TargetInstanceOperation.PrepareExit, operation).ExitPreparation == TargetExitPreparation.Ready, 3000), Is.True);
                // Simulate the authenticated caller's process identity becoming stale, without stopping any process.
                received.Caller.StartTimeUtcTicks--;
                Assert.That(cancelled.Wait(4000), Is.True);
                Assert.That(Send(TargetInstanceOperation.Status).Status.IsExitPrepared, Is.False);
            }
        }

        [Test]
        public void ExitReservationBlocksNewOwnersAndReleasesOnCancel()
        {
            using (var reservation = TargetInstanceCoordinator.TryReserveAllTargets(_channel, _root))
            {
                Assert.That(reservation, Is.Not.Null);
                Assert.That(TargetInstanceCoordinator.TryReserveAllTargets(_channel, _root), Is.Null);
                Assert.Throws<InvalidOperationException>(() => TargetInstanceCoordinator.Acquire(_channel, "wsl-new", _root));
            }
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, "wsl-new", _root))
                Assert.That(owner.IsPrimaryInstance, Is.True);
        }

        [Test]
        public void BoundedPreparationExpiryResumesOwnerAndRejectsOldCommit()
        {
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, "windows", _root))
            using (var cancelled = new ManualResetEventSlim())
            {
                var commits = 0;
                owner.StartServer(request =>
                {
                    if (request.Operation == TargetInstanceOperation.AbortExit) cancelled.Set();
                    if (request.Operation == TargetInstanceOperation.CommitExit) commits++;
                    return Task.FromResult(new TargetInstanceReply { Success = true });
                });
                owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running });
                var operation = Guid.NewGuid().ToString("N");
                Send(TargetInstanceOperation.PrepareExit, operation);
                Assert.That(SpinWait.SpinUntil(() => Send(TargetInstanceOperation.PrepareExit, operation).ExitPreparation == TargetExitPreparation.Ready, 3000), Is.True);
                owner.CheckExitCoordinator(DateTime.UtcNow.AddMinutes(11));
                Assert.That(cancelled.Wait(3000), Is.True);
                Assert.That(Send(TargetInstanceOperation.Status).Status.IsExitPrepared, Is.False);
                Assert.That(Send(TargetInstanceOperation.PrepareExit, operation).ExitPreparation, Is.EqualTo(TargetExitPreparation.Rejected));
                Assert.That(Send(TargetInstanceOperation.CommitExit, operation).Success, Is.False);
                Assert.That(commits, Is.Zero);
            }
        }

        [Test]
        public void ExitAndRemovalPayloadsCannotCrossOperationBoundaries()
        {
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(new TargetInstanceRequest { Operation = TargetInstanceOperation.CommitExit }));
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(new TargetInstanceRequest { Operation = TargetInstanceOperation.Status, OperationId = Guid.NewGuid().ToString("N") }));
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(new TargetInstanceRequest { Operation = TargetInstanceOperation.RemoveTarget, RemovalTargetId = "windows" }));
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(new TargetInstanceRequest { Operation = TargetInstanceOperation.RemoveTarget, RemovalTargetId = "../outside" }));
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(new TargetInstanceRequest { Operation = TargetInstanceOperation.Status, RemovalTargetId = "wsl-one" }));
        }

        private TargetInstanceReply Send(TargetInstanceOperation operation, string operationId = null)
        {
            return TargetInstanceCoordinator.Send(_channel, "windows", _root,
                new TargetInstanceRequest { Operation = operation, OperationId = operationId });
        }
    }
}
