using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetCatalogRemovalTests
    {
        private string _root;
        private AppConfigService _legacy;
        private TargetCatalogStore _catalog;
        private string _channel;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "target-removal-" + Guid.NewGuid().ToString("N"));
            _legacy = new AppConfigService(_root);
            new TargetDataMigration(_legacy).Run();
            _catalog = new TargetCatalogStore(_legacy);
            _channel = "RemovalTest-" + Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void RemoveWindowsOrUnknownTarget_PreservesCatalogAndData()
        {
            var file = Path.Combine(_root, "targets", "catalog.json");
            var before = File.ReadAllBytes(file);
            var identity = File.ReadAllBytes(_catalog.GetPaths("windows").GetRemoteServiceConfigFilePath());
            Assert.Throws<InvalidOperationException>(() => _catalog.RemoveLocalData("windows"));
            Assert.Throws<InvalidOperationException>(() => _catalog.ValidateLocalRemoval("windows"));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => _catalog.RemoveLocalData("wsl-missing"));
            Assert.Throws<ArgumentException>(() => _catalog.RemoveLocalData("../windows"));
            Assert.That(File.ReadAllBytes(file), Is.EqualTo(before));
            Assert.That(File.ReadAllBytes(_catalog.GetPaths("windows").GetRemoteServiceConfigFilePath()), Is.EqualTo(identity));
        }

        [TestCase("Wsl")]
        [TestCase("Ssh")]
        public void RemoveTarget_ResetsLastOpenedAndDoesNotTouchSharedDataOrOutsideDocuments(string kind)
        {
            var target = kind == "Wsl" ? AddLinux() : _catalog.AddSsh("Build", new RemoteMacSshConfigSnapshot
            {
                HostName = "build.example", UserName = "alice", AuthenticationMode = RemoteSshAuthenticationMode.PrivateKey,
                PrivateKeyPath = @"C:\keys\id.ppk"
            });
            var data = _catalog.GetPaths(target.Id).GetAppDataDirectory();
            var shared = Path.Combine(_root, "config", "relay-service.json");
            var sharedBytes = File.ReadAllBytes(shared);
            var project = Path.Combine(_root, "project");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "important.docx"), "keep");
            _catalog.RememberOpened(target.Id);

            var beforeValidation = File.ReadAllBytes(Path.Combine(_root, "targets", "catalog.json"));
            _catalog.ValidateLocalRemoval(target.Id);
            Assert.That(File.ReadAllBytes(Path.Combine(_root, "targets", "catalog.json")), Is.EqualTo(beforeValidation));
            Assert.That(Directory.Exists(data), Is.True);
            Assert.That(_catalog.RemoveLocalData(target.Id), Is.Null);
            Assert.That(_catalog.Load().LastOpenedTargetId, Is.EqualTo("windows"));
            Assert.That(_catalog.Load().Targets.Select(t => t.Id), Is.EqualTo(new[] { "windows" }));
            Assert.That(Directory.Exists(data), Is.False);
            Assert.That(Directory.GetDirectories(Path.Combine(_root, "targets"), ".removed-*"), Is.Empty);
            Assert.That(File.ReadAllBytes(shared), Is.EqualTo(sharedBytes));
            Assert.That(File.ReadAllText(Path.Combine(project, "important.docx")), Is.EqualTo("keep"));
        }

        [TestCase("removal-moved")]
        [TestCase("removal-catalog")]
        public void CatalogFailure_RestoresEntryDataIdentityAndLastOpened(string checkpoint)
        {
            var target = AddLinux();
            var paths = _catalog.GetPaths(target.Id);
            var identityBytes = File.ReadAllBytes(paths.GetRemoteServiceConfigFilePath());
            _catalog.RememberOpened(target.Id);
            var catalogBytes = File.ReadAllBytes(Path.Combine(_root, "targets", "catalog.json"));
            var failing = new TargetCatalogStore(_root, _legacy.GetInstallationDirectory(), "MyCodex", stage =>
            {
                if (stage == checkpoint) throw new IOException("Injected removal publication failure.");
            });

            Assert.Throws<IOException>(() => failing.RemoveLocalData(target.Id));
            Assert.That(_catalog.GetTarget(target.Id).Id, Is.EqualTo(target.Id));
            Assert.That(_catalog.Load().LastOpenedTargetId, Is.EqualTo(target.Id));
            Assert.That(File.ReadAllBytes(paths.GetRemoteServiceConfigFilePath()), Is.EqualTo(identityBytes));
            Assert.That(File.ReadAllBytes(Path.Combine(_root, "targets", "catalog.json")), Is.EqualTo(catalogBytes));
            Assert.That(Directory.GetDirectories(Path.Combine(_root, "targets"), ".removed-*"), Is.Empty);
            Assert.That(_catalog.RemoveLocalData(target.Id), Is.Null);
        }

        [Test]
        public void RemoveOneTarget_PreservesOtherTargetIdentityAndLastOpenedSelection()
        {
            var first = AddLinux();
            var other = _catalog.AddWsl("Other Linux", new WslAppServerConfigSnapshot { DistributionName = "Ubuntu" });
            var otherPaths = _catalog.GetPaths(other.Id);
            var otherIdentity = File.ReadAllBytes(otherPaths.GetRemoteServiceConfigFilePath());
            _catalog.RememberOpened(other.Id);
            Assert.That(_catalog.RemoveLocalData(first.Id), Is.Null);
            Assert.That(_catalog.Load().LastOpenedTargetId, Is.EqualTo(other.Id));
            Assert.That(File.ReadAllBytes(otherPaths.GetRemoteServiceConfigFilePath()), Is.EqualTo(otherIdentity));
        }

        [Test]
        public void CleanupFailure_ReturnsRetainedArchiveAndReaddingNeverRevivesOldHostIdentity()
        {
            var target = AddLinux();
            var identity = new JsonRemoteServiceConfigStore(_catalog.GetPaths(target.Id)).LoadTargetIdentity();
            FileStream locked = null;
            var failing = new TargetCatalogStore(_root, _legacy.GetInstallationDirectory(), "MyCodex", stage =>
            {
                if (stage != "removal-cleanup") return;
                var archive = Directory.GetDirectories(Path.Combine(_root, "targets"), ".removed-*").Single();
                locked = new FileStream(Path.Combine(archive, "config", "remote-service.json"), FileMode.Open, FileAccess.Read, FileShare.None);
            });
            string retained;
            try { retained = failing.RemoveLocalData(target.Id); }
            finally { locked?.Dispose(); }

            Assert.That(retained, Is.Not.Null);
            Assert.That(Directory.Exists(retained), Is.True);
            Assert.That(_catalog.Load().Targets.Any(t => t.Id == target.Id), Is.False);
            var replacement = AddLinux();
            var newIdentity = new JsonRemoteServiceConfigStore(_catalog.GetPaths(replacement.Id)).LoadTargetIdentity();
            Assert.That(newIdentity.HostId, Is.Not.EqualTo(identity.HostId));
            Assert.That(File.Exists(_catalog.GetPaths(replacement.Id).GetRemoteBindingsFilePath()), Is.False);
            Assert.That(newIdentity.RemoteEnabled, Is.False);
        }

        [TestCase("project")]
        [TestCase("office")]
        [TestCase("automation")]
        public void ConfiguredUserDirectoryInsideLocalTarget_IsPreservedWithCatalogEntry(string kind)
        {
            var target = AddLinux();
            var paths = _catalog.GetPaths(target.Id);
            var directory = Path.Combine(paths.GetLogDirectory(), "user-work");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "document.txt"), "keep");
            if (kind == "project")
                Write(paths.GetAppStateFilePath(), new AppStateSnapshot
                {
                    RecentWorkspaces = { new WorkspaceInfo { RootPath = directory, LastOpenedAt = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) } }
                });
            else if (kind == "office")
                Write(paths.GetModeSettingsFilePath(), new ModeSettingsSnapshot { OfficeRootPath = directory });
            else
                Write(paths.GetAutomationsFilePath(), new AutomationStateSnapshot
                {
                    Definitions =
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Context = new AutomationContextSnapshot { WorkspacePath = directory },
                            CreatedAt = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc),
                            UpdatedAt = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
                        }
                    }
                });

            Assert.Throws<IOException>(() => _catalog.ValidateLocalRemoval(target.Id));
            Assert.Throws<IOException>(() => _catalog.RemoveLocalData(target.Id));
            Assert.That(_catalog.GetTarget(target.Id).Id, Is.EqualTo(target.Id));
            Assert.That(File.ReadAllText(Path.Combine(directory, "document.txt")), Is.EqualTo("keep"));
        }

        [TestCase("unknown")]
        [TestCase("config-file")]
        [TestCase("automation-output")]
        public void UnrecognizedOrAutomationUserFiles_AreNotDeleted(string kind)
        {
            var target = AddLinux();
            var paths = _catalog.GetPaths(target.Id);
            var directory = kind == "unknown" ? paths.GetAppDataDirectory() : kind == "config-file" ? paths.GetConfigDirectory() : paths.GetAutomationWorkspaceDirectory();
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "important.txt");
            File.WriteAllText(file, "keep");
            Assert.Throws<IOException>(() => _catalog.RemoveLocalData(target.Id));
            Assert.That(File.ReadAllText(file), Is.EqualTo("keep"));
            Assert.That(_catalog.GetTarget(target.Id).Id, Is.EqualTo(target.Id));
        }

        [Test]
        public void UnreferencedGitRepositoryUnderOwnedDirectory_IsPreserved()
        {
            var target = AddLinux();
            var repository = Path.Combine(_catalog.GetPaths(target.Id).GetLogDirectory(), "project", ".git");
            Directory.CreateDirectory(repository);
            File.WriteAllText(Path.Combine(repository, "HEAD"), "test repository fixture");
            Assert.Throws<IOException>(() => _catalog.ValidateLocalRemoval(target.Id));
            Assert.Throws<IOException>(() => _catalog.RemoveLocalData(target.Id));
            Assert.That(File.Exists(Path.Combine(repository, "HEAD")), Is.True);
            Assert.That(_catalog.GetTarget(target.Id).Id, Is.EqualTo(target.Id));
        }

        [Test]
        public void SelectedSshPrivateKeyInsideLocalTargetIsPreserved()
        {
            var target = _catalog.AddSsh("Build", new RemoteMacSshConfigSnapshot
            {
                HostName = "build.example", UserName = "alice", AuthenticationMode = RemoteSshAuthenticationMode.PrivateKey,
                PrivateKeyPath = @"C:\keys\id.ppk"
            });
            var paths = _catalog.GetPaths(target.Id);
            var key = Path.Combine(paths.GetConfigDirectory(), "test-user-key.ppk");
            File.WriteAllText(key, "test fixture, no private material");
            var settings = _catalog.LoadRuntimeSettings(target.Id);
            settings.RemoteSsh.PrivateKeyPath = key;
            _catalog.SaveRuntimeSettings(target.Id, settings);
            Assert.Throws<IOException>(() => _catalog.RemoveLocalData(target.Id));
            Assert.That(_catalog.GetTarget(target.Id).Id, Is.EqualTo(target.Id));
            Assert.That(File.Exists(key), Is.True);
        }

        [Test]
        public void NestedDirectoryJunctionIsRejectedWithoutTouchingOutsideData()
        {
            var target = AddLinux();
            var outside = Path.Combine(_root, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "document.txt"), "keep");
            var link = Path.Combine(_catalog.GetPaths(target.Id).GetLogDirectory(), "link");
            Directory.CreateDirectory(Path.GetDirectoryName(link));
            using (var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + outside + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            }))
            {
                process.WaitForExit();
                Assume.That(process.ExitCode, Is.Zero, "This environment cannot create a test junction.");
            }
            try
            {
                Assert.Throws<IOException>(() => _catalog.RemoveLocalData(target.Id));
                Assert.That(_catalog.GetTarget(target.Id).Id, Is.EqualTo(target.Id));
                Assert.That(File.ReadAllText(Path.Combine(outside, "document.txt")), Is.EqualTo("keep"));
            }
            finally { Directory.Delete(link, false); }
        }

        [Test]
        public async Task RemovalService_OfflineUnpairedTargetDoesNotContactRelay()
        {
            var target = AddLinux();
            var relay = new RemovalRelayClient();
            var result = await new TargetRemovalService(_catalog, _channel, relay).RemoveAsync(target.Id);
            Assert.That(result.Removed, Is.True);
            Assert.That(result.RelayCleanupPending, Is.False);
            Assert.That(result.RetainedArchivePath, Is.Null);
            Assert.That(relay.RevokeCalls, Is.Zero);
            Assert.That(_catalog.Load().Targets.Any(t => t.Id == target.Id), Is.False);
        }

        [Test]
        public void RemovalService_CatalogFailurePreservesActiveBindingAndNeverRevokesRelay()
        {
            var target = AddLinux();
            var paths = _catalog.GetPaths(target.Id);
            AddTestBinding(paths);
            var before = File.ReadAllBytes(paths.GetRemoteBindingsFilePath());
            var failing = new TargetCatalogStore(_root, _legacy.GetInstallationDirectory(), "MyCodex", stage =>
            {
                if (stage == "removal-catalog") throw new IOException("Injected publication failure.");
            });
            var relay = new RemovalRelayClient();
            Assert.ThrowsAsync<IOException>(() => new TargetRemovalService(failing, _channel, relay).RemoveAsync(target.Id));
            Assert.That(_catalog.GetTarget(target.Id).Id, Is.EqualTo(target.Id));
            Assert.That(File.ReadAllBytes(paths.GetRemoteBindingsFilePath()), Is.EqualTo(before));
            Assert.That(relay.RevokeCalls, Is.Zero);
        }

        [Test]
        public async Task RemovalService_RelayFailureAfterLocalCommitReportsPendingWithoutRevivingTarget()
        {
            var target = AddLinux();
            var paths = _catalog.GetPaths(target.Id);
            var hostId = AddTestBinding(paths);
            var relay = new RemovalRelayClient
            {
                OnRevoke = () => Assert.That(_catalog.Load().Targets.Any(t => t.Id == target.Id), Is.False)
            };
            var result = await new TargetRemovalService(_catalog, _channel, relay).RemoveAsync(target.Id);
            Assert.That(result.Removed, Is.True);
            Assert.That(result.RelayCleanupPending, Is.True);
            Assert.That(relay.RevokeCalls, Is.EqualTo(1));
            Assert.That(relay.LastHostId, Is.EqualTo(hostId));
            Assert.That(File.Exists(paths.GetRemoteBindingsFilePath()), Is.False);
            Assert.That(_catalog.Load().Targets.Any(t => t.Id == target.Id), Is.False);
        }

        private string AddTestBinding(AppConfigService paths)
        {
            var configStore = new JsonRemoteServiceConfigStore(paths);
            var config = configStore.Load();
            config.RemoteServiceMode = "External";
            config.RelayHost = "relay.test.invalid";
            config.TenantId = "fixture-tenant";
            configStore.ProtectAccessSecret(config, "fixture-secret-not-real");
            configStore.Save(config);
            var binding = new RemoteDeviceBinding
            {
                ClaimId = "fixture-claim", TenantId = config.TenantId, HostId = config.HostId,
                DeviceId = "fixture-phone", DeviceSigningPublicKey = "fixture-signing",
                DeviceAgreementPublicKey = "fixture-agreement", DeviceKeyVersion = 1
            };
            var bindings = new JsonRemoteBindingStore(paths.GetRemoteBindingsFilePath());
            bindings.SavePending(binding);
            bindings.Activate(binding.ClaimId, binding.BindingVersion);
            return config.HostId;
        }

        private TargetProfileSnapshot AddLinux()
        {
            return _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
        }

        private static void Write<T>(string path, T value)
        {
            using (var stream = File.Create(path)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        }

        private sealed class RemovalRelayClient : IRelayHostClient
        {
            internal int RevokeCalls;
            internal string LastHostId;
            internal Action OnRevoke;

            public Task<bool> RevokeDeviceAsync(RelayHostConfig config, string hostId, string deviceId, CancellationToken cancellationToken)
            {
                RevokeCalls++;
                LastHostId = hostId;
                OnRevoke?.Invoke();
                return Task.FromResult(false);
            }

            public Task<RelayMetadata> GetMetadataAsync(RelayHostConfig config, CancellationToken cancellationToken) { throw new NotSupportedException(); }
            public Task<RelayHostRegistrationResult> EnrollHostAsync(RelayHostConfig config, RemoteEndpointIdentity identity, CancellationToken cancellationToken) { throw new NotSupportedException(); }
            public Task<RelayPairingInvite> CreatePairingInviteAsync(RelayHostConfig config, CreatePairingInviteRequest request, CancellationToken cancellationToken) { throw new NotSupportedException(); }
            public Task CancelPairingInviteAsync(RelayHostConfig config, string inviteId, CancellationToken cancellationToken) { throw new NotSupportedException(); }
            public Task<IReadOnlyList<OpaquePairingClaim>> ListPairingClaimsAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken) { throw new NotSupportedException(); }
            public Task ApprovePairingClaimAsync(RelayHostConfig config, PairingApprovalRequest request, CancellationToken cancellationToken) { throw new NotSupportedException(); }
            public Task RejectPairingClaimAsync(RelayHostConfig config, string hostId, string claimId, string deviceId, CancellationToken cancellationToken) { throw new NotSupportedException(); }
            public Task<IReadOnlyList<RelayDeviceSummary>> ListDevicesAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken) { throw new NotSupportedException(); }
        }
    }
}
