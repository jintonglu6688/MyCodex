using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetCatalogStoreTests
    {
        private string _root;
        private AppConfigService _legacy;
        private TargetCatalogStore _store;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "target-catalog", Guid.NewGuid().ToString("N"));
            _legacy = new AppConfigService(_root);
            new TargetDataMigration(_legacy).Run();
            _store = new TargetCatalogStore(_legacy);
        }

        [Test]
        public void AddWsl_CreatesCompleteDefaultTargetWithoutInheritedIdentityOrOtherPreferences()
        {
            var originalIdentity = new JsonRemoteServiceConfigStore(_store.GetPaths("windows")).LoadTargetIdentity();
            var windows = new JsonAppConfigStore(_store.GetPaths("windows"));
            var old = windows.Load();
            old.UiThemePreference = "dark";
            old.EnableAutomaticChatReading = true;
            old.MainWindowHotKeyEnabled = true;
            windows.Save(old);
            var target = _store.AddWsl("Work Linux", new WslAppServerConfigSnapshot
            {
                DistributionName = "Debian", ShellCommand = "bash", CodexCommand = "/opt/codex"
            });

            var paths = _store.GetPaths(target.Id);
            var config = new JsonAppConfigStore(paths).Load();
            var identity = new JsonRemoteServiceConfigStore(paths).LoadTargetIdentity();
            Assert.That(target.Id, Is.EqualTo(TargetMigrationPlanner.GetTargetId("Wsl", "wsl|debian")));
            Assert.That(config.CodexRuntimeSource, Is.EqualTo("Wsl"));
            Assert.That(config.WslAppServer.ShellCommand, Is.EqualTo("bash"));
            Assert.That(config.WslAppServer.CodexCommand, Is.EqualTo("/opt/codex"));
            Assert.That(config.UiThemePreference, Is.EqualTo("auto"));
            Assert.That(config.EnableAutomaticChatReading, Is.False);
            Assert.That(config.MainWindowHotKeyEnabled, Is.False);
            Assert.That(identity.HostId, Is.Not.EqualTo(originalIdentity.HostId));
            Assert.That(identity.RemoteEnabled, Is.False);
            Assert.That(File.Exists(paths.GetRemoteBindingsFilePath()), Is.False);
            Assert.That(File.Exists(paths.GetAppStateFilePath()), Is.True);
            Assert.That(File.Exists(paths.GetAutomationsFilePath()), Is.True);
            Assert.That(paths.GetRuntimeDirectory(), Is.EqualTo(_legacy.GetRuntimeDirectory()));
            Assert.That(_store.Load().LastOpenedTargetId, Is.EqualTo("windows"));
        }

        [TestCase("Wsl", null)]
        [TestCase("Wsl", "Shared replies")]
        [TestCase("Ssh", null)]
        [TestCase("Ssh", "Shared replies")]
        public void NewTargetCopiesOnlyWindowsChannelNameAndLaterChannelEditsRemainIndependent(string kind, string name)
        {
            var windows = new JsonAppConfigStore(_store.GetPaths("windows"));
            var config = windows.Load();
            config.ScreenReaderIndependentChannelName = name;
            config.ScreenReaderProvider = ScreenReaderProviderKind.Nvda;
            config.ManualBrowseUseReaderMainChannel = true;
            config.EnableAutomaticChatReading = true;
            windows.Save(config);

            var target = kind == "Wsl"
                ? _store.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" })
                : _store.AddSsh("Build", Ssh("build.example", "alice", 22));
            var targetStore = new JsonAppConfigStore(_store.GetPaths(target.Id));
            var targetConfig = targetStore.Load();
            Assert.That(targetConfig.ScreenReaderIndependentChannelName, Is.EqualTo(name));
            Assert.That(targetConfig.ScreenReaderProvider, Is.EqualTo(ScreenReaderProviderKind.Auto));
            Assert.That(targetConfig.ManualBrowseUseReaderMainChannel, Is.False);
            Assert.That(targetConfig.EnableAutomaticChatReading, Is.False);

            config.ScreenReaderIndependentChannelName = "Windows changed";
            windows.Save(config);
            Assert.That(targetStore.Load().ScreenReaderIndependentChannelName, Is.EqualTo(name));
            targetConfig.ScreenReaderIndependentChannelName = "Target custom";
            targetStore.Save(targetConfig);
            Assert.That(windows.Load().ScreenReaderIndependentChannelName, Is.EqualTo("Windows changed"));
            Assert.That(targetStore.Load().ScreenReaderIndependentChannelName, Is.EqualTo("Target custom"));
        }

        [Test]
        public void WslDuplicate_ReportsExistingIdCaseInsensitive()
        {
            var target = _store.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var error = Assert.Throws<DuplicateTargetException>(() => _store.AddWsl("Duplicate",
                new WslAppServerConfigSnapshot { DistributionName = "debian" }));
            Assert.That(error.ExistingTargetId, Is.EqualTo(target.Id));
            Assert.That(_store.Load().Targets.Count, Is.EqualTo(2));
        }

        [Test]
        public void SshDuplicate_NormalizesHostCaseAndTrailingDotButRetainsUnixUserCase()
        {
            var first = _store.AddSsh("Build", Ssh("Build.Example", "alice", 22));
            var error = Assert.Throws<DuplicateTargetException>(() => _store.AddSsh("Duplicate", Ssh("build.example.", "alice", 22)));
            Assert.That(error.ExistingTargetId, Is.EqualTo(first.Id));
            var otherUser = _store.AddSsh("Other user", Ssh("build.example", "Alice", 22));
            var otherPort = _store.AddSsh("Other port", Ssh("build.example", "alice", 2222));
            Assert.That(new[] { first.Id, otherUser.Id, otherPort.Id }.Distinct().Count(), Is.EqualTo(3));
            var settings = _store.LoadRuntimeSettings(first.Id);
            Assert.That(settings.RemoteSsh.HostName, Is.EqualTo("Build.Example"));
            Assert.That(_store.GetTarget(first.Id).RuntimeScopeId, Is.EqualTo("remote-unix|" + first.SshProfileId));
        }

        [TestCase("Bundled")]
        [TestCase("System")]
        [TestCase("Custom")]
        public void WindowsModeAndRename_PreserveRootIdentityPairingStateAndFreshPreferences(string source)
        {
            var paths = _store.GetPaths("windows");
            var identityStore = new JsonRemoteServiceConfigStore(paths);
            var beforeIdentity = identityStore.LoadTargetIdentity();
            beforeIdentity.RemoteEnabled = true;
            identityStore.SaveTargetIdentity(beforeIdentity);
            File.WriteAllText(paths.GetRemoteBindingsFilePath(), "keep bindings bytes");
            var stateBytes = File.ReadAllBytes(paths.GetAppStateFilePath());
            var sharedBytes = File.ReadAllBytes(Path.Combine(_root, "config", "relay-service.json"));
            var draft = _store.LoadRuntimeSettings("windows");
            var configStore = new JsonAppConfigStore(paths);
            var newerPreferences = configStore.Load();
            newerPreferences.UiThemePreference = "dark";
            newerPreferences.EnableAutomaticChatReading = true;
            configStore.Save(newerPreferences);
            draft.Name = "My PC";
            draft.CodexRuntimeSource = source;
            draft.CodexCustomExecutablePath = @"C:\Apps\codex.exe";
            _store.SaveRuntimeSettings("windows", draft);

            var after = _store.GetPaths("windows");
            var config = new JsonAppConfigStore(after).Load();
            var identity = new JsonRemoteServiceConfigStore(after).LoadTargetIdentity();
            Assert.That(after.GetAppDataDirectory(), Is.EqualTo(paths.GetAppDataDirectory()));
            Assert.That(config.CodexRuntimeSource, Is.EqualTo(source));
            Assert.That(config.UiThemePreference, Is.EqualTo("dark"));
            Assert.That(config.EnableAutomaticChatReading, Is.True);
            Assert.That(identity.HostId, Is.EqualTo(beforeIdentity.HostId));
            Assert.That(identity.RemoteEnabled, Is.True);
            Assert.That(identity.ThisPcName, Is.EqualTo("My PC"));
            Assert.That(_store.GetTarget("windows").Name, Is.EqualTo("My PC"));
            Assert.That(_store.Load().Targets.Count(t => t.Kind == "Windows"), Is.EqualTo(1));
            Assert.That(File.ReadAllText(after.GetRemoteBindingsFilePath()), Is.EqualTo("keep bindings bytes"));
            Assert.That(File.ReadAllBytes(after.GetAppStateFilePath()), Is.EqualTo(stateBytes));
            Assert.That(File.ReadAllBytes(Path.Combine(_root, "config", "relay-service.json")), Is.EqualTo(sharedBytes));
        }

        [Test]
        public void ImmutableIdentityChanges_RejectWithoutChangingFiles()
        {
            var linux = _store.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var linuxPaths = _store.GetPaths(linux.Id);
            var before = File.ReadAllBytes(linuxPaths.GetConfigFilePath());
            var settings = _store.LoadRuntimeSettings(linux.Id);
            settings.WslAppServer.DistributionName = "Ubuntu";
            Assert.Throws<ArgumentException>(() => _store.SaveRuntimeSettings(linux.Id, settings));
            Assert.That(File.ReadAllBytes(linuxPaths.GetConfigFilePath()), Is.EqualTo(before));

            var ssh = _store.AddSsh("Build", Ssh("build.example", "alice", 22));
            settings = _store.LoadRuntimeSettings(ssh.Id);
            settings.RemoteSsh.UserName = "bob";
            Assert.Throws<ArgumentException>(() => _store.SaveRuntimeSettings(ssh.Id, settings));
            Assert.That(_store.LoadRuntimeSettings(ssh.Id).RemoteSsh.UserName, Is.EqualTo("alice"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SshEndpointEdit_DuplicateRejectsBeforeSaveAndOriginalProfileRemains(bool changeHost)
        {
            var first = _store.AddSsh("One", Ssh("example", "alice", 22));
            var second = _store.AddSsh("Two", Ssh(changeHost ? "other.example" : "example", "alice", changeHost ? 22 : 2222));
            var before = File.ReadAllBytes(_store.GetPaths(second.Id).GetConfigFilePath());
            var settings = _store.LoadRuntimeSettings(second.Id);
            settings.RemoteSsh.Port = 22;
            settings.RemoteSsh.HostName = "example";
            var error = Assert.Throws<DuplicateTargetException>(() => _store.SaveRuntimeSettings(second.Id, settings));
            Assert.That(error.ExistingTargetId, Is.EqualTo(first.Id));
            Assert.That(File.ReadAllBytes(_store.GetPaths(second.Id).GetConfigFilePath()), Is.EqualTo(before));
            Assert.That(_store.GetTarget(second.Id).SshProfileId, Is.EqualTo(second.SshProfileId));
        }

        [Test]
        public void SshPassword_IsProtectedAndInvalidPlaintextCannotReachPersistence()
        {
            var ssh = Ssh("example", "alice", 22);
            ssh.AuthenticationMode = RemoteSshAuthenticationMode.Password;
            ssh.ProtectedPassword = "plaintext";
            Assert.Catch(() => _store.AddSsh("Build", ssh));
            Assert.That(_store.Load().Targets.Count, Is.EqualTo(1));
            ssh.ProtectedPassword = RemoteSshPasswordProtector.Protect("test-password");
            var target = _store.AddSsh("Build", ssh);
            var paths = _store.GetPaths(target.Id);
            Assert.That(File.ReadAllText(paths.GetConfigFilePath()), Does.Not.Contain("test-password"));
            Assert.That(RemoteSshPasswordProtector.Unprotect(_store.LoadRuntimeSettings(target.Id).RemoteSsh.ProtectedPassword), Is.EqualTo("test-password"));
            var before = File.ReadAllBytes(paths.GetConfigFilePath());
            var settings = _store.LoadRuntimeSettings(target.Id);
            settings.RemoteSsh.ProtectedPassword = "another-plaintext";
            Assert.Catch(() => _store.SaveRuntimeSettings(target.Id, settings));
            Assert.That(File.ReadAllBytes(paths.GetConfigFilePath()), Is.EqualTo(before));
        }

        [Test]
        public void ConcurrentAddsAndRename_RereadCatalogWithoutLosingOtherTargets()
        {
            var other = new TargetCatalogStore(_legacy);
            Task.WaitAll(
                Task.Run(() => _store.AddWsl("Debian", new WslAppServerConfigSnapshot { DistributionName = "Debian" })),
                Task.Run(() => other.AddWsl("Ubuntu", new WslAppServerConfigSnapshot { DistributionName = "Ubuntu" })));
            var settings = _store.LoadRuntimeSettings("windows");
            settings.Name = "Renamed PC";
            Task.WaitAll(Task.Run(() => _store.SaveRuntimeSettings("windows", settings)),
                Task.Run(() => other.AddSsh("Build", Ssh("example", "alice", 22))));
            Assert.That(_store.Load().Targets.Count, Is.EqualTo(4));
            Assert.That(other.GetTarget("windows").Name, Is.EqualTo("Renamed PC"));
        }

        [Test]
        public void ConcurrentDuplicateAdd_PublishesOneCompleteTarget()
        {
            var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                try { new TargetCatalogStore(_legacy).AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" }); return true; }
                catch (DuplicateTargetException) { return false; }
            })).ToArray();
            Task.WaitAll(tasks);
            Assert.That(tasks.Count(t => t.Result), Is.EqualTo(1));
            Assert.That(_store.Load().Targets.Count, Is.EqualTo(2));
            Assert.That(Directory.GetDirectories(Path.Combine(_root, "targets"), ".adding-*"), Is.Empty);
        }

        [Test]
        public void MissingTargetDataAndUnknownTarget_DoNotFallbackToWindows()
        {
            var linux = _store.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var paths = _store.GetPaths(linux.Id);
            File.Delete(paths.GetAppStateFilePath());
            Assert.Throws<FileNotFoundException>(() => _store.GetPaths(linux.Id));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => _store.GetPaths("missing"));
            Assert.Throws<ArgumentException>(() => _store.GetPaths("../windows"));
            Assert.That(_store.Load().Targets.Count, Is.EqualTo(2));
            Assert.That(_store.GetPaths("windows"), Is.Not.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrCorruptSharedConfig_FailsStrictlyWithoutReplacingCredentials(bool corrupt)
        {
            var path = Path.Combine(_root, "config", "relay-service.json");
            if (corrupt) File.WriteAllText(path, "{invalid");
            else File.Delete(path);
            Assert.Catch(() => _store.Load());
            Assert.That(corrupt ? File.ReadAllText(path) : null, Is.EqualTo(corrupt ? "{invalid" : null));
            Assert.That(File.Exists(path), Is.EqualTo(corrupt));
        }

        [Test]
        public void ExistingUnregisteredDirectory_IsPreservedAndNeverRegisteredOrOverwritten()
        {
            var id = TargetMigrationPlanner.GetTargetId("Wsl", "wsl|debian");
            var path = Path.Combine(_root, "targets", id);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "keep.txt"), "user data");
            Assert.Throws<IOException>(() => _store.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" }));
            Assert.That(_store.Load().Targets.Count, Is.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(path, "keep.txt")), Is.EqualTo("user data"));
        }

        [Test]
        public void CatalogTraversalEntry_IsRejectedBeforeReadingOutsideRoot()
        {
            var catalog = _store.Load();
            catalog.Targets.Add(new TargetProfileSnapshot { Id = "../outside", Kind = "Wsl", Name = "Invalid", RuntimeScopeId = "wsl|evil", WslDistributionName = "evil" });
            using (var stream = File.Create(Path.Combine(_root, "targets", "catalog.json")))
                new DataContractJsonSerializer(typeof(TargetCatalogSnapshot)).WriteObject(stream, catalog);
            Assert.Throws<InvalidDataException>(() => _store.Load());
        }

        [Test]
        public void RememberOpened_UsesLatestCatalogAndRejectsUnknownTarget()
        {
            var linux = _store.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            _store.RememberOpened(linux.Id);
            Assert.That(new TargetCatalogStore(_legacy).Load().LastOpenedTargetId, Is.EqualTo(linux.Id));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => _store.RememberOpened("missing"));
            Assert.That(_store.Load().LastOpenedTargetId, Is.EqualTo(linux.Id));
        }

        [Test]
        public void ConcurrentSameTargetRuntimeEdit_RejectsStaleDraftBeforeAnyFileChanges()
        {
            var linux = _store.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var stale = _store.LoadRuntimeSettings(linux.Id);
            stale.Name = "Stale rename";
            var fresh = new TargetCatalogStore(_legacy).LoadRuntimeSettings(linux.Id);
            fresh.WslAppServer.CodexCommand = "new-command";
            new TargetCatalogStore(_legacy).SaveRuntimeSettings(linux.Id, fresh);
            var paths = _store.GetPaths(linux.Id);
            var configBytes = File.ReadAllBytes(paths.GetConfigFilePath());
            var identityBytes = File.ReadAllBytes(paths.GetRemoteServiceConfigFilePath());
            Assert.Throws<TargetRuntimeSettingsConflictException>(() => _store.SaveRuntimeSettings(linux.Id, stale));
            Assert.That(File.ReadAllBytes(paths.GetConfigFilePath()), Is.EqualTo(configBytes));
            Assert.That(File.ReadAllBytes(paths.GetRemoteServiceConfigFilePath()), Is.EqualTo(identityBytes));
            Assert.That(_store.GetTarget(linux.Id).Name, Is.EqualTo("Linux"));
            Assert.That(_store.LoadRuntimeSettings(linux.Id).WslAppServer.CodexCommand, Is.EqualTo("new-command"));
            fresh = _store.LoadRuntimeSettings(linux.Id);
            fresh.ExpectedRuntimeRevision = null;
            Assert.Throws<TargetRuntimeSettingsConflictException>(() => _store.SaveRuntimeSettings(linux.Id, fresh));
        }

        [TestCase("config")]
        [TestCase("identity")]
        [TestCase("catalog")]
        public void SaveFailure_RestoresAllAttemptedFilesAndOriginalRevisionCanRetry(string checkpoint)
        {
            var linux = _store.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var paths = _store.GetPaths(linux.Id);
            var files = new[] { paths.GetConfigFilePath(), paths.GetRemoteServiceConfigFilePath(), Path.Combine(_root, "targets", "catalog.json") };
            var originals = files.Select(File.ReadAllBytes).ToArray();
            var settings = _store.LoadRuntimeSettings(linux.Id);
            settings.Name = "Renamed";
            settings.WslAppServer.CodexCommand = "new-command";
            var failing = new TargetCatalogStore(_root, _legacy.GetInstallationDirectory(), "MyCodex", stage =>
            {
                if (stage == checkpoint) throw new IOException("Injected target save failure.");
            });
            Assert.Throws<IOException>(() => failing.SaveRuntimeSettings(linux.Id, settings));
            for (var index = 0; index < files.Length; index++) Assert.That(File.ReadAllBytes(files[index]), Is.EqualTo(originals[index]));
            _store.SaveRuntimeSettings(linux.Id, settings);
            Assert.That(_store.GetTarget(linux.Id).Name, Is.EqualTo("Renamed"));
            Assert.That(_store.LoadRuntimeSettings(linux.Id).WslAppServer.CodexCommand, Is.EqualTo("new-command"));
        }

        [Test]
        public void SaveRollback_HoldsIdentityMutexSoBackgroundRegistrationFieldsAreNotLost()
        {
            var linux = _store.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var paths = _store.GetPaths(linux.Id);
            var settings = _store.LoadRuntimeSettings(linux.Id);
            settings.Name = "Cancelled rename";
            Task background = null;
            using (var started = new ManualResetEventSlim())
            {
                var failing = new TargetCatalogStore(_root, _legacy.GetInstallationDirectory(), "MyCodex", stage =>
                {
                    if (stage != "config") return;
                    background = Task.Run(() =>
                    {
                        started.Set();
                        var store = new JsonRemoteServiceConfigStore(paths);
                        var identity = store.LoadTargetIdentity();
                        identity.RemoteEnabled = true;
                        store.SaveTargetIdentity(identity);
                    });
                    Assert.That(started.Wait(2000), Is.True);
                    Assert.That(background.Wait(100), Is.False, "A registration writer must wait for rollback to release the identity lock.");
                    throw new IOException("Injected save failure.");
                });
                Assert.Throws<IOException>(() => failing.SaveRuntimeSettings(linux.Id, settings));
                Assert.That(background.Wait(5000), Is.True);
            }
            var current = new JsonRemoteServiceConfigStore(paths).LoadTargetIdentity();
            Assert.That(current.RemoteEnabled, Is.True);
            Assert.That(current.ThisPcName, Is.EqualTo("Linux"));
            Assert.That(_store.GetTarget(linux.Id).Name, Is.EqualTo("Linux"));
        }

        [Test]
        public void RollbackFailure_IsExplicitAndDoesNotGenerateReplacementIdentityOrDefaults()
        {
            var paths = _store.GetPaths("windows");
            var identityBytes = File.ReadAllBytes(paths.GetRemoteServiceConfigFilePath());
            var catalogBytes = File.ReadAllBytes(Path.Combine(_root, "targets", "catalog.json"));
            var settings = _store.LoadRuntimeSettings("windows");
            settings.Name = "Attempted rename";
            var failing = new TargetCatalogStore(_root, _legacy.GetInstallationDirectory(), "MyCodex", stage =>
            {
                if (stage != "config") return;
                File.Delete(paths.GetConfigFilePath());
                Directory.CreateDirectory(paths.GetConfigFilePath());
                throw new IOException("Injected failure that also prevents restoration.");
            });
            var error = Assert.Throws<AggregateException>(() => failing.SaveRuntimeSettings("windows", settings));
            Assert.That(error.Message, Does.Contain("could not be fully restored"));
            Assert.That(error.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(File.ReadAllBytes(paths.GetRemoteServiceConfigFilePath()), Is.EqualTo(identityBytes));
            Assert.That(File.ReadAllBytes(Path.Combine(_root, "targets", "catalog.json")), Is.EqualTo(catalogBytes));
            Assert.That(File.Exists(paths.GetConfigFilePath()), Is.False);
            Assert.Catch(() => _store.GetPaths("windows"));
        }

        [Test]
        public void FailedSameNameTargetFallsBackWithoutChangingNewerSelectionOrTargetData()
        {
            var failed = _store.AddSsh("Mac", Ssh("192.168.50.3", "test", 22));
            var good = _store.AddSsh("Mac", Ssh("test.invalid", "test", 22));
            var configPath = _store.GetPaths(failed.Id).GetConfigFilePath();
            var original = File.ReadAllBytes(configPath);
            Assert.That(failed.Id, Is.Not.EqualTo(good.Id));
            _store.RememberOpened(failed.Id);
            _store.ResetLastOpenedAfterFailure(failed.Id);
            Assert.That(_store.Load().LastOpenedTargetId, Is.EqualTo("windows"));
            Assert.That(_store.Load().Targets.Count, Is.EqualTo(3));
            Assert.That(File.ReadAllBytes(configPath), Is.EqualTo(original));
            _store.RememberOpened(good.Id);
            _store.ResetLastOpenedAfterFailure(failed.Id);
            Assert.That(_store.Load().LastOpenedTargetId, Is.EqualTo(good.Id));
        }

        private static RemoteMacSshConfigSnapshot Ssh(string host, string user, int port)
        {
            return new RemoteMacSshConfigSnapshot { HostName = host, UserName = user, Port = port,
                AuthenticationMode = RemoteSshAuthenticationMode.PrivateKey, PrivateKeyPath = @"C:\keys\id.ppk" };
        }
    }
}
