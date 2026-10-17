using System;
using System.IO;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class TargetWindowLauncherTests
    {
        private string _root;
        private string _channel;
        private TargetCatalogStore _catalog;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "target-window-launcher", Guid.NewGuid().ToString("N"));
            _channel = "LauncherTest-" + Guid.NewGuid().ToString("N");
            var paths = new AppConfigService(_root);
            new TargetDataMigration(paths).Run();
            _catalog = new TargetCatalogStore(paths);
        }

        [Test]
        public async Task OpenRunningTarget_ActivatesVerifiedOwnerOnceAndNeverStartsAnotherProcess()
        {
            var linux = _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, linux.Id, _root))
            {
                var count = 0;
                owner.StartServer(request =>
                {
                    count++;
                    Assert.That(request.Operation, Is.EqualTo(TargetInstanceOperation.Activate));
                    Assert.That(request.WorkspacePath, Is.EqualTo("/home/alice/project"));
                    return Task.FromResult(new TargetInstanceReply { Success = true });
                });
                owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running, IsWorking = true });
                var launcher = new TargetWindowLauncher(_catalog, _channel, _root, _ => Assert.Fail("A second process must not launch."));
                var reply = await launcher.OpenAsync(linux.Id, @"\\wsl$\Debian\home\alice\project");
                Assert.That(reply.Success, Is.True, reply.Error);
                Assert.That(count, Is.EqualTo(1));
                Assert.That(_catalog.Load().LastOpenedTargetId, Is.EqualTo(linux.Id));
                Assert.That(TargetInstanceCoordinator.Send(_channel, linux.Id, _root,
                    new TargetInstanceRequest { Operation = TargetInstanceOperation.Status }).Status.IsWorking, Is.True);
            }
        }

        [Test]
        public async Task OpenOfflineTarget_PassesExplicitChannelTargetAndWorkspaceThenWaitsForActualRunningStatus()
        {
            var linux = _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            TargetInstanceCoordinator owner = null;
            string[] launched = null;
            try
            {
                var launcher = new TargetWindowLauncher(_catalog, _channel, _root, args =>
                {
                    launched = args;
                    owner = TargetInstanceCoordinator.Acquire(_channel, linux.Id, _root);
                    Assert.That(owner.IsPrimaryInstance, Is.True, "The launch reservation must be released before child startup.");
                    owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running });
                });
                var reply = await launcher.OpenAsync(linux.Id, "/home/alice/project with spaces");
                Assert.That(reply.Success, Is.True, reply.Error);
                Assert.That(launched, Is.EqualTo(new[] { "--channel", _channel, "--target", linux.Id, "--workspace", "/home/alice/project with spaces" }));
                Assert.That(_catalog.Load().LastOpenedTargetId, Is.EqualTo(linux.Id));
            }
            finally { owner?.Dispose(); }
        }

        [Test]
        public async Task StartupWaitDoesNotExpireWhileTheVerifiedWindowsOwnerPreparesItsRuntime()
        {
            TargetInstanceCoordinator owner = null;
            Task ready = null;
            try
            {
                var launcher = new TargetWindowLauncher(_catalog, _channel, _root, args =>
                {
                    owner = TargetInstanceCoordinator.Acquire(_channel, "windows", _root);
                    owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Starting, IsPreparingBundledRuntime = true });
                    ready = Task.Run(async () =>
                    {
                        await Task.Delay(400);
                        owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running });
                    });
                }, 150);
                var reply = await launcher.OpenAsync("windows");
                Assert.That(reply.Success, Is.True, reply.Error);
                Assert.That(reply.Status.Lifecycle, Is.EqualTo(TargetInstanceLifecycle.Running));
            }
            finally { if (ready != null) await ready; owner?.Dispose(); }
        }

        [TestCase("Bundled", true)]
        [TestCase("System", false)]
        public void LongSaveDeadlineIsAllowedOnlyForTheManagedWindowsRuntime(string source, bool allowed)
        {
            TestDelegate send = () => TargetInstanceCoordinator.Send(_channel, "windows", _root,
                new TargetInstanceRequest { Operation = TargetInstanceOperation.ApplyRuntimeSettings,
                    RuntimeSettings = new TargetRuntimeSettings { CodexRuntimeSource = source } }, 15 * 60 * 1000);
            if (allowed) Assert.DoesNotThrow(send);
            else Assert.Throws<ArgumentOutOfRangeException>(send);
        }

        [TestCase(TargetInstanceLifecycle.Starting)]
        [TestCase(TargetInstanceLifecycle.Closing)]
        public async Task OccupiedStartingOrClosingTarget_IsNotRelaunchedOrRemembered(TargetInstanceLifecycle lifecycle)
        {
            var linux = _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, linux.Id, _root))
            {
                owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = lifecycle });
                var launcher = new TargetWindowLauncher(_catalog, _channel, _root, _ => Assert.Fail("Occupied target cannot relaunch."));
                var reply = await launcher.OpenAsync(linux.Id);
                Assert.That(reply.Success, Is.False);
                Assert.That(_catalog.Load().LastOpenedTargetId, Is.EqualTo("windows"));
            }
        }

        [Test]
        public async Task StartupNotConfirmed_DoesNotRememberOrAttemptSecondLaunch()
        {
            var linux = _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var launches = 0;
            var launcher = new TargetWindowLauncher(_catalog, _channel, _root, _ => launches++, 150);
            var reply = await launcher.OpenAsync(linux.Id);
            Assert.That(reply.Success, Is.False);
            Assert.That(launches, Is.EqualTo(1));
            Assert.That(reply.Error, Does.Contain("has not confirmed startup"));
            Assert.That(_catalog.Load().LastOpenedTargetId, Is.EqualTo("windows"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NewlyStartedTargetClosing_CancelsOpeningWithoutReportingAnError(bool ssh)
        {
            var target = ssh
                ? _catalog.AddSsh("Mac", new RemoteMacSshConfigSnapshot { HostName = "test.invalid", UserName = "test" })
                : _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            TargetInstanceCoordinator owner = null;
            var launches = 0;
            try
            {
                var launcher = new TargetWindowLauncher(_catalog, _channel, _root, args =>
                {
                    launches++;
                    owner = TargetInstanceCoordinator.Acquire(_channel, target.Id, _root);
                    owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Closing });
                });
                var opening = launcher.OpenAsync(target.Id);
                Assert.ThrowsAsync<OperationCanceledException>(async () => await opening);
                Assert.That(opening.IsCanceled, Is.True, "Closing must neither report failure nor authorize operations on a running target.");
                Assert.That(launches, Is.EqualTo(1));
                Assert.That(_catalog.Load().LastOpenedTargetId, Is.EqualTo("windows"));
            }
            finally { owner?.Dispose(); }
        }

        [Test]
        public async Task InvalidTargetOrWorkspace_IsRejectedBeforeAnyLaunch()
        {
            var linux = _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var launcher = new TargetWindowLauncher(_catalog, _channel, _root, _ => Assert.Fail("Invalid input cannot launch."));
            Assert.That((await launcher.OpenAsync(linux.Id, @"C:\Windows\project")).Success, Is.False);
            Assert.That((await launcher.OpenAsync("../windows")).Success, Is.False);
            Assert.That((await launcher.OpenAsync("missing")).Success, Is.False);
            File.Delete(_catalog.GetPaths(linux.Id).GetRemoteServiceConfigFilePath());
            Assert.That((await launcher.OpenAsync(linux.Id)).Success, Is.False);
            Assert.That(_catalog.Load().LastOpenedTargetId, Is.EqualTo("windows"));
        }

        [Test]
        public async Task ProcessStartFailureRemainsAnErrorRatherThanCancellation()
        {
            var target = _catalog.AddSsh("Mac", new RemoteMacSshConfigSnapshot { HostName = "test.invalid", UserName = "test" });
            var launcher = new TargetWindowLauncher(_catalog, _channel, _root, args => { throw new InvalidOperationException("Injected start failure."); });
            var opening = launcher.OpenAsync(target.Id);
            var reply = await opening;
            Assert.That(opening.IsCanceled, Is.False);
            Assert.That(reply.Success, Is.False);
            Assert.That(reply.Error, Does.Contain("could not be started"));
            Assert.That(_catalog.Load().LastOpenedTargetId, Is.EqualTo("windows"));
        }

        [Test]
        public async Task SaveOffline_ReadsFreshPreferencesWhileHoldingSameInstanceReservation()
        {
            var linux = _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var draft = _catalog.LoadRuntimeSettings(linux.Id);
            draft.Name = "Renamed Linux";
            var paths = _catalog.GetPaths(linux.Id);
            var configStore = new JsonAppConfigStore(paths);
            var fresh = configStore.Load();
            fresh.EnableAutomaticChatReading = true;
            fresh.UiThemePreference = "dark";
            configStore.Save(fresh);
            var identity = new JsonRemoteServiceConfigStore(paths).LoadTargetIdentity();
            var launcher = new TargetWindowLauncher(_catalog, _channel, _root, _ => Assert.Fail("Saving must not launch."));
            var reply = await launcher.SaveAsync(linux.Id, draft);
            Assert.That(reply.Success, Is.True, reply.Error);
            Assert.That(reply.SettingsSaved, Is.True);
            Assert.That(_catalog.GetTarget(linux.Id).Name, Is.EqualTo("Renamed Linux"));
            Assert.That(configStore.Load().UiThemePreference, Is.EqualTo("dark"));
            Assert.That(configStore.Load().EnableAutomaticChatReading, Is.True);
            Assert.That(new JsonRemoteServiceConfigStore(paths).LoadTargetIdentity().HostId, Is.EqualTo(identity.HostId));
            Assert.That(_catalog.Load().LastOpenedTargetId, Is.EqualTo("windows"));
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, linux.Id, _root)) Assert.That(owner.IsPrimaryInstance, Is.True);
        }

        [Test]
        public async Task SaveRunningTarget_SendsFiniteSettingsOnlyAndDoesNotWriteFromManager()
        {
            var linux = _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var draft = _catalog.LoadRuntimeSettings(linux.Id);
            draft.Name = "Requested rename";
            var configBytes = File.ReadAllBytes(_catalog.GetPaths(linux.Id).GetConfigFilePath());
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, linux.Id, _root))
            {
                var count = 0;
                owner.StartServer(request =>
                {
                    count++;
                    Assert.That(request.Operation, Is.EqualTo(TargetInstanceOperation.ApplyRuntimeSettings));
                    Assert.That(request.RuntimeSettings.Name, Is.EqualTo("Requested rename"));
                    Assert.That(request.RuntimeSettings.WslAppServer.DistributionName, Is.EqualTo("Debian"));
                    return Task.FromResult(new TargetInstanceReply { Success = false, Error = "User cancelled" });
                });
                owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running });
                var launcher = new TargetWindowLauncher(_catalog, _channel, _root, _ => Assert.Fail("Saving cannot launch."));
                var reply = await launcher.SaveAsync(linux.Id, draft);
                Assert.That(reply.Success, Is.False);
                Assert.That(reply.Error, Is.EqualTo("User cancelled"));
                Assert.That(count, Is.EqualTo(1));
                Assert.That(_catalog.GetTarget(linux.Id).Name, Is.EqualTo("Linux"));
                Assert.That(File.ReadAllBytes(_catalog.GetPaths(linux.Id).GetConfigFilePath()), Is.EqualTo(configBytes));
            }
        }

        [TestCase(TargetInstanceLifecycle.Starting)]
        [TestCase(TargetInstanceLifecycle.Running)]
        [TestCase(TargetInstanceLifecycle.Closing)]
        public async Task SshHostChange_RequiresClosedTargetAndDoesNotSendLiveEdit(TargetInstanceLifecycle lifecycle)
        {
            var target = _catalog.AddSsh("Mac", new RemoteMacSshConfigSnapshot { HostName = "wrong.example", UserName = "test", HostKeyFingerprint = "old-key" });
            var draft = _catalog.LoadRuntimeSettings(target.Id);
            draft.RemoteSsh.HostName = "correct.example";
            var paths = _catalog.GetPaths(target.Id);
            var configBytes = File.ReadAllBytes(paths.GetConfigFilePath());
            var stateBytes = File.ReadAllBytes(paths.GetAppStateFilePath());
            var identity = new JsonRemoteServiceConfigStore(paths).LoadTargetIdentity();
            File.WriteAllText(paths.GetRemoteBindingsFilePath(), "keep pairings");
            var launcher = new TargetWindowLauncher(_catalog, _channel, _root, _ => Assert.Fail("Saving cannot launch."));
            using (var owner = TargetInstanceCoordinator.Acquire(_channel, target.Id, _root))
            {
                var requests = 0;
                owner.StartServer(request => { requests++; return Task.FromResult(TargetInstanceReply.Failure("Unexpected live edit")); });
                owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = lifecycle });
                var rejected = await launcher.SaveAsync(target.Id, draft);
                Assert.That(requests, Is.Zero, "Host edits cannot be sent to a live owner.");
                Assert.That(rejected.Success, Is.False);
                Assert.That(rejected.SettingsSaved, Is.False);
                Assert.That(rejected.Error, Is.Not.Empty);
                Assert.That(File.ReadAllBytes(paths.GetConfigFilePath()), Is.EqualTo(configBytes));
            }
            var saved = await launcher.SaveAsync(target.Id, draft);
            Assert.That(saved.Success, Is.True, saved.Error);
            Assert.That(saved.SettingsSaved, Is.True);
            Assert.That(_catalog.LoadRuntimeSettings(target.Id).RemoteSsh.HostName, Is.EqualTo("correct.example"));
            Assert.That(_catalog.LoadRuntimeSettings(target.Id).RemoteSsh.HostKeyFingerprint, Is.Null);
            Assert.That(_catalog.GetTarget(target.Id).RuntimeScopeId, Is.EqualTo(target.RuntimeScopeId));
            Assert.That(File.ReadAllBytes(paths.GetAppStateFilePath()), Is.EqualTo(stateBytes));
            var savedIdentity = new JsonRemoteServiceConfigStore(paths).LoadTargetIdentity();
            Assert.That(savedIdentity.HostId, Is.EqualTo(identity.HostId));
            Assert.That(savedIdentity.CreatedAt, Is.EqualTo(identity.CreatedAt));
            Assert.That(savedIdentity.RemoteEnabled, Is.EqualTo(identity.RemoteEnabled));
            Assert.That(File.ReadAllText(paths.GetRemoteBindingsFilePath()), Is.EqualTo("keep pairings"));
        }

        [Test]
        public async Task SaveOfflineInvalidEdit_PreservesOriginalAndReleasesReservation()
        {
            var linux = _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var draft = _catalog.LoadRuntimeSettings(linux.Id);
            draft.WslAppServer.DistributionName = "Ubuntu";
            var launcher = new TargetWindowLauncher(_catalog, _channel, _root, _ => Assert.Fail("Saving cannot launch."));
            var reply = await launcher.SaveAsync(linux.Id, draft);
            Assert.That(reply.Success, Is.False);
            Assert.That(_catalog.LoadRuntimeSettings(linux.Id).WslAppServer.DistributionName, Is.EqualTo("Debian"));
            using (var reservation = TargetInstanceCoordinator.TryReserveOfflineEdit(_channel, linux.Id, _root))
                Assert.That(reservation, Is.Not.Null);
        }
    }
}
