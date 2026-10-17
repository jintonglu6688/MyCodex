using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationHostUsageTests
    {
        private string _root;
        private string _dataDirectory;
        private string _usersDirectory;
        private Dictionary<int, AuthenticationHostUsage.Identity> _processes;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-users-" + Guid.NewGuid().ToString("N"));
            _dataDirectory = Path.Combine(_root, "backend");
            _usersDirectory = Path.Combine(_root, "backend.desktop-users");
            _processes = new Dictionary<int, AuthenticationHostUsage.Identity>();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public async Task DesktopCoordinationLeavesFreshBackendStoreEmpty()
        {
            Directory.CreateDirectory(_dataDirectory);
            var usage = CreateUsage(11, "MyCodex.exe");
            using (await usage.EnterAsync(CancellationToken.None))
            {
                Assert.That(Directory.GetFileSystemEntries(_dataDirectory), Is.Empty,
                    "Native startup must be able to establish ownership of its empty store.");
                usage.Register();
                Assert.That(Directory.GetFileSystemEntries(_dataDirectory), Is.Empty);
                Assert.That(usage.ReleaseAndIsLastUser(), Is.True);
            }
        }

        [Test]
        public async Task FailedFirstStartCoordinationCanBeRetainedOutsideTheBackendStore()
        {
            var legacyDirectory = Path.Combine(_dataDirectory, "desktop-users");
            Directory.CreateDirectory(legacyDirectory);
            File.WriteAllText(Path.Combine(legacyDirectory, "lifecycle.lock"), string.Empty);
            var usage = CreateUsage(11, "MyCodex.exe");
            using (await usage.EnterAsync(CancellationToken.None))
            {
                Assert.That(Directory.GetFileSystemEntries(_dataDirectory), Is.Empty);
                Assert.That(File.Exists(Path.Combine(_usersDirectory, "lifecycle.lock")), Is.True);
            }
        }

        [Test]
        public void RecoveryWaitsForALegacyFirstStartAndHonorsCancellation()
        {
            var legacyDirectory = Path.Combine(_dataDirectory, "desktop-users");
            Directory.CreateDirectory(legacyDirectory);
            using (var legacyGate = new FileStream(Path.Combine(legacyDirectory, "lifecycle.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            using (var timeout = new CancellationTokenSource())
            {
                var waiting = CreateUsage(11, "MyCodex.exe").EnterAsync(timeout.Token);
                Assert.That(waiting.IsCompleted, Is.False);
                timeout.Cancel();
                Assert.ThrowsAsync<TaskCanceledException>(async () => await waiting);
                Assert.That(Directory.Exists(legacyDirectory), Is.True);
            }
        }

        [TestCase("native-file")]
        [TestCase("unknown-user-record")]
        [TestCase("nonempty-lock")]
        public async Task RecoveryNeverMovesUnknownStoreContent(string content)
        {
            var legacyDirectory = Path.Combine(_dataDirectory, "desktop-users");
            Directory.CreateDirectory(legacyDirectory);
            var contentPath = content == "native-file" ? Path.Combine(_dataDirectory, "private-data")
                : Path.Combine(legacyDirectory, content == "nonempty-lock" ? "lifecycle.lock" : "unknown.json");
            File.WriteAllText(Path.Combine(legacyDirectory, "lifecycle.lock"), string.Empty);
            File.WriteAllText(contentPath, "retained fixture");
            using (await CreateUsage(11, "MyCodex.exe").EnterAsync(CancellationToken.None))
            {
                Assert.That(Directory.Exists(legacyDirectory), Is.True);
                Assert.That(File.ReadAllText(contentPath), Is.EqualTo("retained fixture"));
            }
        }

        [Test]
        public async Task RecoveryNeverMovesADirectoryNamedLikeTheLock()
        {
            var lockDirectory = Path.Combine(_dataDirectory, "desktop-users", "lifecycle.lock");
            Directory.CreateDirectory(lockDirectory);
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            using (await CreateUsage(11, "MyCodex.exe").EnterAsync(timeout.Token))
                Assert.That(Directory.Exists(lockDirectory), Is.True);
        }

        [Test]
        public void EstablishedLegacyStoreKeepsItsExistingLifecycleGate()
        {
            Directory.CreateDirectory(_dataDirectory);
            File.WriteAllText(Path.Combine(_dataDirectory, ".mycodex-auth-core"), "owned store fixture");
            var legacyDirectory = Path.Combine(_dataDirectory, "desktop-users");
            Directory.CreateDirectory(legacyDirectory);
            using (var legacyGate = new FileStream(Path.Combine(legacyDirectory, "lifecycle.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            using (var timeout = new CancellationTokenSource())
            {
                var waiting = CreateUsage(11, "MyCodex.exe").EnterAsync(timeout.Token);
                Assert.That(waiting.IsCompleted, Is.False);
                timeout.Cancel();
                Assert.ThrowsAsync<TaskCanceledException>(async () => await waiting);
            }
        }

        [Test]
        public async Task EquivalentStorePathsShareTheLifecycleGate()
        {
            var first = CreateUsage(11, "MyCodex.exe");
            var second = new AuthenticationHostUsage(_dataDirectory.ToUpperInvariant() + Path.DirectorySeparatorChar,
                Identity(12, "MyCodex-dev.exe"), id => _processes.TryGetValue(id, out var value) ? value : null);
            using (await first.EnterAsync(CancellationToken.None))
            using (var timeout = new CancellationTokenSource())
            {
                var waiting = second.EnterAsync(timeout.Token);
                Assert.That(waiting.IsCompleted, Is.False);
                timeout.Cancel();
                Assert.ThrowsAsync<TaskCanceledException>(async () => await waiting);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DevAndStableSharingAHomeKeepBackendUntilLastUserExits(bool legacyStore)
        {
            if (legacyStore)
            {
                _usersDirectory = Path.Combine(_dataDirectory, "desktop-users");
                Directory.CreateDirectory(_usersDirectory);
                File.WriteAllText(Path.Combine(_dataDirectory, ".mycodex-auth-core"), "owned store fixture");
            }
            var dev = CreateUsage(11, "MyCodex-dev.exe");
            var stable = CreateUsage(12, "MyCodex.exe");
            using (await dev.EnterAsync(CancellationToken.None)) dev.Register();
            using (await stable.EnterAsync(CancellationToken.None)) stable.Register();
            using (await dev.EnterAsync(CancellationToken.None))
                Assert.That(dev.ReleaseAndIsLastUser(), Is.False);
            using (await stable.EnterAsync(CancellationToken.None))
                Assert.That(stable.ReleaseAndIsLastUser(), Is.True);
        }

        [Test]
        public async Task SeparateHostsWithinOneProcessAndReconnectDoNotLoseUsage()
        {
            var first = CreateUsage(11, "MyCodex.exe");
            var second = CreateUsage(11, "MyCodex.exe");
            using (await first.EnterAsync(CancellationToken.None))
            {
                first.Register();
                first.Register();
            }
            using (await second.EnterAsync(CancellationToken.None)) second.Register();
            Assert.That(Directory.GetFiles(_usersDirectory, "*.json"), Has.Length.EqualTo(2));
            using (await first.EnterAsync(CancellationToken.None))
                Assert.That(first.ReleaseAndIsLastUser(), Is.False);
            using (await second.EnterAsync(CancellationToken.None))
                Assert.That(second.ReleaseAndIsLastUser(), Is.True);
        }

        [Test]
        public async Task DifferentCodexHomesHaveIndependentOwnership()
        {
            var first = CreateUsage(11, "MyCodex.exe");
            var second = new AuthenticationHostUsage(Path.Combine(_root, "other-home"), Identity(12, "MyCodex-dev.exe"),
                id => _processes.TryGetValue(id, out var value) ? value : null);
            using (await first.EnterAsync(CancellationToken.None)) first.Register();
            using (await second.EnterAsync(CancellationToken.None)) second.Register();
            using (await first.EnterAsync(CancellationToken.None))
                Assert.That(first.ReleaseAndIsLastUser(), Is.True);
            using (await second.EnterAsync(CancellationToken.None))
                Assert.That(second.ReleaseAndIsLastUser(), Is.True);
        }

        [TestCase("exited")]
        [TestCase("reused-pid")]
        [TestCase("different-path")]
        public async Task StaleOwnersCannotPreventRecovery(string stale)
        {
            var crashed = CreateUsage(11, "MyCodex.exe");
            using (await crashed.EnterAsync(CancellationToken.None)) crashed.Register();
            if (stale == "exited") _processes.Remove(11);
            else if (stale == "reused-pid") _processes[11].StartTimeUtcTicks++;
            else _processes[11].ExecutablePath = Path.Combine(_root, "unrelated.exe");
            var replacement = CreateUsage(12, "MyCodex-dev.exe");
            using (await replacement.EnterAsync(CancellationToken.None))
            {
                replacement.Register();
                Assert.That(replacement.ReleaseAndIsLastUser(), Is.True);
            }
            Assert.That(Directory.GetFiles(_usersDirectory, "*.json"), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UnknownOwnershipNeverGrantsShutdown(bool damagedRecord)
        {
            var other = CreateUsage(11, "MyCodex.exe");
            using (await other.EnterAsync(CancellationToken.None)) other.Register();
            if (damagedRecord)
                File.WriteAllText(Directory.GetFiles(_usersDirectory, "*.json")[0], "invalid");
            var exiting = new AuthenticationHostUsage(_dataDirectory,
                Identity(12, "MyCodex-dev.exe"), id => throw new UnauthorizedAccessException());
            using (await exiting.EnterAsync(CancellationToken.None))
            {
                exiting.Register();
                Assert.That(exiting.ReleaseAndIsLastUser(), Is.False);
            }
        }

        [Test]
        public async Task ConcurrentStartWaitsForExitAndHonorsCancellation()
        {
            var first = CreateUsage(11, "MyCodex.exe");
            var second = CreateUsage(12, "MyCodex-dev.exe");
            using (await first.EnterAsync(CancellationToken.None))
            using (var timeout = new CancellationTokenSource())
            {
                var waiting = second.EnterAsync(timeout.Token);
                Assert.That(waiting.IsCompleted, Is.False);
                timeout.Cancel();
                Assert.ThrowsAsync<TaskCanceledException>(async () => await waiting);
            }
            using (await second.EnterAsync(CancellationToken.None))
            {
                second.Register();
                Assert.That(second.ReleaseAndIsLastUser(), Is.True);
            }
        }

        private AuthenticationHostUsage CreateUsage(int processId, string executable)
        {
            var identity = Identity(processId, executable);
            _processes[processId] = identity;
            return new AuthenticationHostUsage(_dataDirectory, identity, id =>
                _processes.TryGetValue(id, out var value) ? value : null);
        }

        private AuthenticationHostUsage.Identity Identity(int processId, string executable)
        {
            return new AuthenticationHostUsage.Identity
            {
                ProcessId = processId,
                ExecutablePath = Path.Combine(_root, executable),
                StartTimeUtcTicks = 638877312001234567L
            };
        }
    }
}
