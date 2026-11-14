using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.Infrastructure.Instances;
using NUnit.Framework;
using State = MyCodex.Infrastructure.AuthenticationCenter.AuthenticationInstallationStateStore.State;
using Target = MyCodex.Infrastructure.AuthenticationCenter.AuthenticationInstallationStateStore.Target;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationInstallationMaintenanceTests
    {
        private string _root;
        private AuthenticationInstallationStateStore _store;
        private Dictionary<string, object> _manifest;
        private List<Target> _running;
        private List<Target> _registered;
        private List<string> _operations;
        private bool _failRestore;
        private bool _failStop;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-install-" + Guid.NewGuid().ToString("N"));
            _store = new AuthenticationInstallationStateStore(Path.Combine(_root, "installed"), Path.Combine(_root, "state.json"));
            Directory.CreateDirectory(_store.PackageDirectory);
            File.WriteAllText(_store.Executable, "fixture, never executed");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(_store.Executable))).Replace("-", "");
            _manifest = new Dictionary<string, object>
            {
                { "schemaVersion", 1 }, { "hostVersion", "0.4.1" }, { "protocolVersion", 3 },
                { "upstreamRevision", "85ad4969b3399500baaea9d46b1e08f87aba80e1" },
                { "sourceRevision", new string('a', 40) }, { "sourceDirty", false },
                { "target", "x86_64-pc-windows-msvc" }, { "executable", "mycodex-auth-host.exe" }, { "sha256", hash }
            };
            WriteManifest();
            File.WriteAllText(Path.Combine(_store.PackageDirectory, "LICENSE"), "fixture");
            _running = new List<Target>();
            _registered = new List<Target>();
            _operations = new List<string>();
            _failRestore = _failStop = false;
        }

        [TearDown]
        public void TearDown() { Directory.Delete(_root, true); }

        private AuthenticationInstallationMaintenance Maintenance()
        {
            return new AuthenticationInstallationMaintenance(_store.Root, _store.Path,
                () => _running, () => _registered,
                (target, uninstall, token) =>
                {
                    _operations.Add((uninstall ? "uninstall " : "stop ") + target.home);
                    if (_failStop) throw new AuthenticationCenterException("route_requests_active");
                    return Task.CompletedTask;
                },
                (target, older, token) =>
                {
                    _operations.Add((older ? "resume-old " : "resume-current ") + target.home);
                    if (_failRestore) throw new AuthenticationCenterException("host_start_failed");
                    return Task.CompletedTask;
                }, () => _operations.Add("clear-registration"));
        }

        private Target TargetFor(string name) => new Target { home = Path.Combine(_root, name, "home"), store = Path.Combine(_root, name, "store") };
        private Task<int> Run(string action) => Maintenance().RunAsync(action, CancellationToken.None);
        private void WriteManifest() => File.WriteAllText(Path.Combine(_store.PackageDirectory, AuthenticationHostArtifact.ManifestFileName), new JavaScriptSerializer().Serialize(_manifest));
        private void BeginCopy() => File.Copy(Path.Combine(_store.PackageDirectory, AuthenticationHostArtifact.ManifestFileName), _store.ExpectedPath, true);

        [Test]
        public async Task FinalizeOnlyChecksFilesWhenNoBackendWasNeeded()
        {
            Assert.That(await Run("Prepare"), Is.Zero);
            BeginCopy();
            Assert.That(await Run("Finalize"), Is.Zero);
            Assert.That(_store.Read().completed, Is.True);
            Assert.That(_operations, Is.Empty, "A fresh installation must not launch the backend just to verify it.");
        }

        [Test]
        public async Task StartupFailureRetainsTargetsAndNextResumeCompletes()
        {
            _running.Add(TargetFor("one"));
            _registered.Add(TargetFor("two"));
            Assert.That(await Run("Prepare"), Is.Zero);
            BeginCopy();
            _failRestore = true;
            Assert.That(await Run("Finalize"), Is.EqualTo(2));
            Assert.That(_store.Read().completed, Is.False);
            Assert.That(_store.Read().targets.Count, Is.EqualTo(2));
            Assert.That(File.Exists(_store.ExpectedPath), Is.True);
            _failRestore = false;
            Assert.That(await Run("Resume"), Is.Zero);
            Assert.That(_store.Read().completed, Is.True);
            Assert.That(File.Exists(_store.ExpectedPath), Is.False);
        }

        [Test]
        public async Task FailureBeforeFinalizeEntryIsRecoverableFromInstallerMarker()
        {
            _running.Add(TargetFor("one"));
            await Run("Prepare");
            BeginCopy();
            // No Finalize call: models the helper being rejected or terminated before its first log entry.
            Assert.That(await Run("Resume"), Is.Zero);
            Assert.That(_operations.Last(), Does.StartWith("resume-current "));
            Assert.That(_store.Read().completed, Is.True);
        }

        [Test]
        public async Task CorruptInstalledFilesCannotRestoreOldBackupAfterCopyStarted()
        {
            await Run("Prepare");
            BeginCopy();
            File.WriteAllText(_store.Executable, "corrupt");
            Assert.That(await Run("Finalize"), Is.EqualTo(1));
            Assert.That(await Run("Resume"), Is.EqualTo(1));
            Assert.That(File.ReadAllText(_store.Executable), Is.EqualTo("corrupt"));
            Assert.That(_operations, Is.Empty);
        }

        [Test]
        public async Task ChangedPackageIdentityDoesNotStartBackend()
        {
            _running.Add(TargetFor("one"));
            await Run("Prepare");
            BeginCopy();
            _manifest["sourceRevision"] = new string('b', 40);
            WriteManifest();
            Assert.That(await Run("Finalize"), Is.EqualTo(1));
            Assert.That(_operations.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task PreCopyCancellationCanResumeOlderInstalledBackend()
        {
            _manifest["hostVersion"] = "0.3.12";
            WriteManifest();
            _running.Add(TargetFor("one"));
            await Run("Prepare");
            File.WriteAllText(_store.Executable, "interrupted old installation");
            Assert.That(await Run("Resume"), Is.Zero);
            Assert.That(_operations.Last(), Does.StartWith("resume-old "));
            Assert.That(File.ReadAllText(_store.Executable), Is.EqualTo("fixture, never executed"));
        }

        [Test]
        public async Task RetryingPreparationRetainsPreviouslyStoppedTargets()
        {
            _running.Add(TargetFor("one"));
            await Run("Prepare");
            _running.Clear();
            Assert.That(await Run("Prepare"), Is.Zero);
            Assert.That(_store.Read().targets.Count, Is.EqualTo(1));
            Assert.That(await Run("Resume"), Is.Zero);
        }

        [Test]
        public async Task CompletedOlderInstallerStateDoesNotPreventCancellationBeforeNewCopy()
        {
            _manifest["hostVersion"] = "0.3.12";
            WriteManifest();
            _store.Write(new State { installRoot = _store.Root, completed = true, expectedPackage = _manifest });
            _running.Add(TargetFor("older"));
            Assert.That(await Run("Prepare"), Is.Zero);
            Assert.That(await Run("Resume"), Is.Zero);
            Assert.That(_operations.Last(), Does.StartWith("resume-old "));
        }

        [Test]
        public async Task RegisteredAndStoppedCopiesOfSameTargetRestartOnce()
        {
            _running.Add(TargetFor("one"));
            _registered.Add(TargetFor("one"));
            await Run("Prepare");
            BeginCopy();
            Assert.That(await Run("Finalize"), Is.Zero);
            Assert.That(_operations.Count(x => x.StartsWith("resume-", StringComparison.Ordinal)), Is.EqualTo(1));
        }

        [Test]
        public async Task CancelledRecoveryRetainsRemainingTargetsWithoutStartingThem()
        {
            _running.Add(TargetFor("one"));
            _running.Add(TargetFor("two"));
            await Run("Prepare");
            BeginCopy();
            using (var timeout = new CancellationTokenSource())
            {
                var starts = 0;
                var maintenance = new AuthenticationInstallationMaintenance(_store.Root, _store.Path,
                    findRegistered: () => _registered, restore: (target, older, token) =>
                    {
                        starts++;
                        timeout.Cancel();
                        throw new OperationCanceledException(token);
                    });
                Assert.That(await maintenance.RunAsync("Finalize", timeout.Token), Is.EqualTo(2));
                Assert.That(starts, Is.EqualTo(1));
                Assert.That(_store.Read().targets.Count, Is.EqualTo(2));
                Assert.That(_store.Read().completed, Is.False);
            }
        }

        [Test]
        public async Task UnexpectedErrorsDoNotExposeTheirMessagesInDiagnostics()
        {
            var maintenance = new AuthenticationInstallationMaintenance(_store.Root, _store.Path,
                findRunning: () => { throw new InvalidOperationException("synthetic-private-command"); });
            Assert.That(await maintenance.RunAsync("Prepare", CancellationToken.None), Is.EqualTo(1));
            var log = File.ReadAllText(_store.Path + ".log");
            Assert.That(log, Does.Contain("Prepare started"));
            Assert.That(log, Does.Contain("authentication_maintenance_failed"));
            Assert.That(log, Does.Not.Contain("synthetic-private-command"));
        }

        [Test]
        public void UnreadableProcessOnlyBlocksWhenInstallationImageIsLocked()
        {
            var legacy = Path.Combine(_store.Root, "runtime", "auth-center", "mycodex-auth-host.exe");
            Assert.That(AuthenticationInstallationProcesses.OwnsPath(null, _store.Executable, legacy, () => false), Is.False);
            Assert.Throws<AuthenticationCenterException>(() => AuthenticationInstallationProcesses.OwnsPath(null, _store.Executable, legacy, () => true));
            Assert.That(AuthenticationInstallationProcesses.OwnsPath(Path.Combine(_root, "foreign.exe"), _store.Executable, legacy, () => true), Is.False);
            Assert.That(AuthenticationInstallationProcesses.OwnsPath(_store.Executable, _store.Executable, legacy, () => false), Is.True);
            Assert.Throws<AuthenticationCenterException>(() => AuthenticationInstallationProcesses.OwnsPath(legacy, _store.Executable, legacy, () => false));
        }

        [Test]
        public void StartupRecoveryCannotRunWhileInstallerOwnsSharedResources()
        {
            using (var reservation = TargetInstanceCoordinator.TryReserveSharedUpdate())
            {
                Assert.That(reservation, Is.Not.Null);
                Assert.That(AuthenticationHostStartup.TryRun(new[] { "--resume-authentication-hosts" }, out var code), Is.True);
                Assert.That(code, Is.EqualTo(1));
                var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => AuthenticationCenterClient.EnsureLocalBackendAsync(CancellationToken.None));
                Assert.That(error.Code, Is.EqualTo("authentication_backend_in_use"));
            }
        }

        [Test]
        public async Task ActiveRequestsBlockUninstallWithoutRemovingRegistration()
        {
            _registered.Add(TargetFor("registered"));
            _failStop = true;
            Assert.That(await Run("Uninstall"), Is.EqualTo(1));
            Assert.That(_operations, Does.Not.Contain("clear-registration"));
            Assert.That(_store.Read().completed, Is.False);
        }

        [Test]
        public async Task SuccessfulUninstallRemovesOnlyItsRegistrationAfterStops()
        {
            _registered.Add(TargetFor("registered"));
            Assert.That(await Run("Uninstall"), Is.Zero);
            Assert.That(_operations.Last(), Is.EqualTo("clear-registration"));
            Assert.That(_store.Read().completed, Is.True);
        }

        [Test]
        public async Task ForeignInstallationStateIsNeverOverwritten()
        {
            _store.Write(new State { installRoot = Path.Combine(_root, "foreign") });
            var original = File.ReadAllText(_store.Path);
            Assert.That(await Run("Prepare"), Is.EqualTo(1));
            Assert.That(File.ReadAllText(_store.Path), Is.EqualTo(original));
        }

        [Test]
        public void RecoveryCopiesCannotWriteToAnotherInstallation()
        {
            var foreign = Path.Combine(_root, "foreign");
            Assert.Throws<AuthenticationCenterException>(() => _store.CopyPackage(_store.PackageDirectory, foreign));
            Assert.That(Directory.Exists(foreign), Is.False);
        }

        [Test]
        public async Task IncompleteUnusedPackageDoesNotBlockRepairPreparation()
        {
            File.WriteAllText(_store.Executable, "incomplete");
            Assert.That(await Run("Prepare"), Is.Zero);
            Assert.That(_operations, Is.Empty);
            _running.Add(TargetFor("running"));
            Assert.That(await Run("Prepare"), Is.EqualTo(1));
        }

        [TestCase("serve --codex-home C:\\Home --data-dir C:\\Store")]
        [TestCase("serve --data-dir C:\\Store --codex-home C:\\Home")]
        public void ProcessArgumentsAcceptEitherOrder(string arguments)
        {
            var target = AuthenticationInstallationProcesses.Parse("host.exe " + arguments);
            Assert.That(target.home, Is.EqualTo(@"C:\Home"));
            Assert.That(target.store, Is.EqualTo(@"C:\Store"));
        }

        [TestCase("rpc --data-dir C:\\Store --codex-home C:\\Home")]
        [TestCase("serve --data-dir C:\\Store --data-dir C:\\Home")]
        [TestCase("serve --data-dir relative --codex-home C:\\Home")]
        [TestCase("serve --data-dir C:\\Store --codex-home C:\\Home extra")]
        public void UnknownProcessArgumentsCannotAuthorizeShutdown(string arguments)
        {
            Assert.Throws<AuthenticationCenterException>(() => AuthenticationInstallationProcesses.Parse("host.exe " + arguments));
        }
    }
}
