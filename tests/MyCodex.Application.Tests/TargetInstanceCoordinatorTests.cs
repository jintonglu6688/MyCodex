using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.Instances;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class TargetInstanceCoordinatorTests
    {
        private string _root;
        private string _channel;

        [SetUp]
        public void SetUp()
        {
            _channel = "InstanceTest-" + Guid.NewGuid().ToString("N");
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "target-instances-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_root, true);
        }

        [Test]
        public void ConcurrentOpensHaveOneOwnerAndOtherTargetsRemainIndependent()
        {
            var contenders = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => TargetInstanceCoordinator.Acquire(_channel, "windows", _root))).ToArray();
            Task.WaitAll(contenders);
            var instances = contenders.Select(task => task.Result).ToArray();
            try
            {
                Assert.That(instances.Count(instance => instance.IsPrimaryInstance), Is.EqualTo(1));
                using (var other = TargetInstanceCoordinator.Acquire(_channel, "wsl-one", _root))
                using (var channel = TargetInstanceCoordinator.Acquire(_channel + "-Dev", "windows", _root))
                {
                    Assert.That(other.IsPrimaryInstance, Is.True);
                    Assert.That(channel.IsPrimaryInstance, Is.True);
                }
            }
            finally { foreach (var instance in instances) instance.Dispose(); }
        }

        [Test]
        public void OfflineEditAndOwnerUseSameGateAndDisposeFromAnotherThread()
        {
            using (var reservation = TargetInstanceCoordinator.TryReserveOfflineEdit(_channel, "windows", _root))
            using (var blocked = TargetInstanceCoordinator.Acquire(_channel, "windows", _root))
            {
                Assert.That(reservation, Is.Not.Null);
                Assert.That(blocked.IsPrimaryInstance, Is.False);
            }
            var owner = TargetInstanceCoordinator.Acquire(_channel, "windows", _root);
            Assert.That(TargetInstanceCoordinator.TryReserveOfflineEdit(_channel, "windows", _root), Is.Null);
            Task.Run(() => owner.Dispose()).GetAwaiter().GetResult();
            using (var replacement = TargetInstanceCoordinator.Acquire(_channel, "windows", _root))
                Assert.That(replacement.IsPrimaryInstance, Is.True);
        }

        [Test]
        public void NativePipeReportsActualStatusAndRejectsStartingAndClosingActivation()
        {
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, "windows", _root))
            {
                var count = 0;
                owner.StartServer(request =>
                {
                    count++;
                    Assert.That(request.WorkspacePath, Is.EqualTo(@"C:\work with spaces"));
                    return Task.FromResult(new TargetInstanceReply { Success = true });
                });
                var starting = Send(TargetInstanceOperation.Status);
                Assert.That(starting.Success, Is.True, starting.Error);
                Assert.That(starting.Status.Lifecycle, Is.EqualTo(TargetInstanceLifecycle.Starting));
                Assert.That(starting.Status.IsWorking, Is.False);
                Assert.That(Send(TargetInstanceOperation.Activate).Success, Is.False);
                var state = new TargetInstanceStatus
                {
                    Lifecycle = TargetInstanceLifecycle.Running, IsWorking = true,
                    HasPendingInteraction = true, IsConnected = false, ConnectionDetail = "Connection failed"
                };
                owner.UpdateStatus(state);
                state.IsWorking = false;
                var running = Send(TargetInstanceOperation.Status);
                Assert.That(running.Status.IsWorking, Is.True, running.Error);
                Assert.That(running.Status.HasPendingInteraction, Is.True);
                Assert.That(running.Status.IsConnected, Is.False);
                Assert.That(running.Status.ConnectionDetail, Is.EqualTo("Connection failed"));
                var activation = TargetInstanceCoordinator.Send(_channel, "windows", _root,
                    new TargetInstanceRequest { Operation = TargetInstanceOperation.Activate, WorkspacePath = @"C:\work with spaces" });
                Assert.That(activation.Success, Is.True, activation.Error);
                Assert.That(count, Is.EqualTo(1));
                owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Closing });
                Assert.That(Send(TargetInstanceOperation.Activate).Success, Is.False);
                Assert.That(Send(TargetInstanceOperation.Status).Status.Lifecycle, Is.EqualTo(TargetInstanceLifecycle.Closing));
                Assert.That(count, Is.EqualTo(1));
                Assert.That(TargetInstanceCoordinator.TryReserveOfflineEdit(_channel, "windows", _root), Is.Null);
            }
        }

        [Test]
        public void FiniteSettingsRequestReachesOnlyNamedOwner()
        {
            using (var first = TargetInstanceCoordinator.Acquire(_channel, "windows", _root))
            using (var second = TargetInstanceCoordinator.Acquire(_channel, "wsl-one", _root))
            {
                var changed = 0;
                first.StartServer(request =>
                {
                    Assert.That(request.Operation, Is.EqualTo(TargetInstanceOperation.ApplyRuntimeSettings));
                    Assert.That(request.RuntimeSettings.Name, Is.EqualTo("Renamed Windows"));
                    changed++;
                    return Task.FromResult(new TargetInstanceReply { Success = true });
                });
                first.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running });
                second.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running, IsWorking = true });
                var reply = TargetInstanceCoordinator.Send(_channel, "windows", _root, new TargetInstanceRequest
                {
                    Operation = TargetInstanceOperation.ApplyRuntimeSettings,
                    RuntimeSettings = new TargetRuntimeSettings { Name = "Renamed Windows" }
                });
                Assert.That(reply.Success, Is.True, reply.Error);
                Assert.That(changed, Is.EqualTo(1));
                var discovery = TargetInstanceCoordinator.Discover(_channel, _root, 3000);
                Assert.That(discovery.Count, Is.EqualTo(2));
                Assert.That(discovery.Single(item => item.Instance.TargetId == "wsl-one").Status.IsWorking, Is.True);
            }
        }

        [Test]
        public void UnknownIdentityBlocksOfflineSaveAndStalePidRecordCanRecover()
        {
            var directory = Path.Combine(_root, "targets", ".instances");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, TargetInstanceSecurity.Route(_channel, "windows") + ".json");
            File.WriteAllText(path, "{}");
            Assert.Throws<InvalidOperationException>(() => TargetInstanceCoordinator.TryReserveOfflineEdit(_channel, "windows", _root));
            var stale = TargetInstanceSecurity.Capture(_channel, "windows");
            stale.StartTimeUtcTicks--;
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(stale));
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, "windows", _root))
                Assert.That(owner.IsPrimaryInstance, Is.True);
        }

        [Test]
        public void SpoofedOwnerRecordCannotSendToVerifiedPipe()
        {
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, "windows", _root))
            {
                var path = Path.Combine(_root, "targets", ".instances", TargetInstanceSecurity.Route(_channel, "windows") + ".json");
                var original = File.ReadAllText(path);
                var record = new JavaScriptSerializer().Deserialize<TargetInstanceRecord>(original);
                record.InstanceId = Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllText(path, new JavaScriptSerializer().Serialize(record));
                    Assert.That(Send(TargetInstanceOperation.Status).Success, Is.False);
                }
                finally { File.WriteAllText(path, original); }
            }
        }

        [Test]
        public void ShutdownCancelsListenerWhileOwnerAwaitsUiConfirmation()
        {
            var entered = new TaskCompletionSource<bool>();
            var confirmation = new TaskCompletionSource<TargetInstanceReply>();
            var owner = TargetInstanceCoordinator.Acquire(_channel, "windows", _root);
            try
            {
                owner.StartServer(request => { entered.TrySetResult(true); return confirmation.Task; });
                owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running });
                var requestTask = Task.Run(() => TargetInstanceCoordinator.Send(_channel, "windows", _root,
                    new TargetInstanceRequest { Operation = TargetInstanceOperation.Activate }));
                Assert.That(entered.Task.Wait(3000), Is.True);
                var shutdown = Task.Run(() => owner.Dispose());
                Assert.That(shutdown.Wait(3000), Is.True, "Listener must not wait for a closing UI thread.");
                Assert.That(requestTask.GetAwaiter().GetResult().Success, Is.False);
                using (var next = TargetInstanceCoordinator.Acquire(_channel, "windows", _root))
                    Assert.That(next.IsPrimaryInstance, Is.True);
            }
            finally
            {
                confirmation.TrySetResult(TargetInstanceReply.Failure("Cancelled"));
                owner.Dispose();
            }
        }

        [Test]
        public void VoiceControl_UsesTheVerifiedOwnerPipeAndPreservesConversationBinding()
        {
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, "windows", _root))
            {
                owner.StartServer(request =>
                {
                    Assert.That(request.Operation, Is.EqualTo(TargetInstanceOperation.VoiceControl));
                    Assert.That(request.VoiceThreadId, Is.EqualTo("exact-thread"));
                    Assert.That(VoiceControlCommand.Parse(request.VoiceArguments).Enabled, Is.False);
                    return Task.FromResult(new TargetInstanceReply { Success = true, VoiceThreadId = request.VoiceThreadId, VoiceResult = "enabled=False" });
                });
                owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running });
                var reply = TargetInstanceCoordinator.Send(_channel, "windows", _root, new TargetInstanceRequest
                {
                    Operation = TargetInstanceOperation.VoiceControl, VoiceThreadId = "exact-thread",
                    VoiceArguments = new System.Collections.Generic.Dictionary<string, object> { { "enabled", false } }
                });
                Assert.That(reply.Success, Is.True, reply.Error);
                Assert.That(reply.VoiceThreadId, Is.EqualTo("exact-thread"));
                Assert.That(reply.VoiceResult, Is.EqualTo("enabled=False"));
            }
        }

        [Test]
        public void UnsupportedOperationsAndCrossOperationPayloadsAreRejected()
        {
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(
                new TargetInstanceRequest { Operation = (TargetInstanceOperation)99 }));
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(
                new TargetInstanceRequest { Operation = TargetInstanceOperation.Status, WorkspacePath = "anything" }));
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(
                new TargetInstanceRequest { Operation = TargetInstanceOperation.Activate, RuntimeSettings = new TargetRuntimeSettings() }));
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(
                new TargetInstanceRequest { Operation = TargetInstanceOperation.ApplyRuntimeSettings }));
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(
                new TargetInstanceRequest { Operation = TargetInstanceOperation.Activate, WorkspacePath = "bad\0path" }));
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(
                new TargetInstanceRequest { Operation = TargetInstanceOperation.Status, VoiceArguments = new System.Collections.Generic.Dictionary<string, object>() }));
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(
                new TargetInstanceRequest { Operation = TargetInstanceOperation.VoiceControl }));
            Assert.Throws<InvalidDataException>(() => TargetInstanceCoordinator.ValidateRequest(
                new TargetInstanceRequest { Operation = TargetInstanceOperation.VoiceControl, VoiceArguments = new System.Collections.Generic.Dictionary<string, object> { { "enabled", true } } }));
        }

        private TargetInstanceReply Send(TargetInstanceOperation operation)
        {
            return TargetInstanceCoordinator.Send(_channel, "windows", _root, new TargetInstanceRequest { Operation = operation });
        }
    }
}
