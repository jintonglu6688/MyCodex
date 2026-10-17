using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Codex;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class BundledRuntimeUpdateTests
    {
        [TestCase(CodexRuntimeSources.System)]
        [TestCase(CodexRuntimeSources.Custom)]
        [TestCase(CodexRuntimeSources.Wsl)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh)]
        public async Task OtherRuntimeSourcesNeverCheckOrInstall(string source)
        {
            var coordinator = new BundledRuntimeUpdateCoordinator(token => throw new AssertionException("Must not check."),
                (info, token) => throw new AssertionException("Must not install."));
            Assert.That(await coordinator.EnsureAsync(source, CancellationToken.None), Is.True);
        }

        [TestCase("0.162.0", "0.162.1", true)]
        [TestCase("0.162.1", "0.162.1", false)]
        [TestCase("0.163.0", "0.162.1", false)]
        [TestCase(null, "0.162.1", true)]
        [TestCase("0.162.1", null, false)]
        public void OnlyNewerOrMissingRuntimeOffersAnUpdate(string installed, string latest, bool required)
        {
            Assert.That(new CodexRuntimeUpdateInfo { InstalledVersion = installed, LatestVersion = latest }.IsUpdateAvailable,
                Is.EqualTo(required));
        }

        [Test]
        public async Task SuccessfulCheckRunsOnceUntilTheUserSwitchesBackToBundled()
        {
            var checks = 0;
            var installs = 0;
            var coordinator = new BundledRuntimeUpdateCoordinator(token =>
            {
                checks++;
                return Task.FromResult(Update());
            }, (info, token) => { installs++; return Task.FromResult(true); });
            Assert.That(await coordinator.EnsureAsync(CodexRuntimeSources.Bundled, CancellationToken.None), Is.True);
            Assert.That(await coordinator.EnsureAsync(CodexRuntimeSources.Bundled, CancellationToken.None), Is.True);
            Assert.That(checks, Is.EqualTo(1));
            Assert.That(installs, Is.EqualTo(1));
            await coordinator.EnsureAsync(CodexRuntimeSources.System, CancellationToken.None);
            await coordinator.EnsureAsync(CodexRuntimeSources.Bundled, CancellationToken.None);
            Assert.That(checks, Is.EqualTo(2));
            Assert.That(installs, Is.EqualTo(2));
        }

        [Test]
        public async Task FailedCheckSilentlyContinuesWithoutAnInstallDialog()
        {
            var coordinator = new BundledRuntimeUpdateCoordinator(token => throw new IOException("offline"),
                (info, token) => throw new AssertionException("Must not show an update dialog."));
            Assert.That(await coordinator.EnsureAsync(CodexRuntimeSources.Bundled, CancellationToken.None), Is.True);
        }

        [Test]
        public async Task DecliningOrCancellingAnUpdateContinuesWithoutPromptingAgain()
        {
            var checks = 0;
            var installs = 0;
            var coordinator = new BundledRuntimeUpdateCoordinator(token =>
            {
                checks++;
                return Task.FromResult(Update());
            }, (info, token) => { installs++; return Task.FromResult(false); });
            Assert.That(await coordinator.EnsureAsync(CodexRuntimeSources.Bundled, CancellationToken.None), Is.True);
            Assert.That(await coordinator.EnsureAsync(CodexRuntimeSources.Bundled, CancellationToken.None), Is.True);
            Assert.That(installs, Is.EqualTo(1));
            Assert.That(checks, Is.EqualTo(1));
            Assert.That(coordinator.HasPendingUpdate, Is.False);
            Assert.That(coordinator.NeedsPreparation, Is.False);
            await coordinator.EnsureAsync(CodexRuntimeSources.System, CancellationToken.None);
            await coordinator.EnsureAsync(CodexRuntimeSources.Bundled, CancellationToken.None);
            Assert.That(installs, Is.EqualTo(2));
            Assert.That(checks, Is.EqualTo(2));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ConcurrentConnectionsWaitForOneUpdateDecision(bool accepted)
        {
            var finished = new TaskCompletionSource<bool>();
            var installs = 0;
            var coordinator = new BundledRuntimeUpdateCoordinator(token => Task.FromResult(Update()),
                (info, token) => { installs++; return finished.Task; });
            var first = coordinator.EnsureAsync(CodexRuntimeSources.Bundled, CancellationToken.None);
            var second = coordinator.EnsureAsync(CodexRuntimeSources.Bundled, CancellationToken.None);
            Assert.That(first.IsCompleted || second.IsCompleted, Is.False);
            Assert.That(coordinator.HasPendingUpdate, Is.True);
            finished.SetResult(accepted);
            Assert.That(await first && await second, Is.True);
            Assert.That(installs, Is.EqualTo(1));
            Assert.That(coordinator.HasPendingUpdate, Is.False);
        }

        [Test]
        public void ShutdownIsNotTreatedAsAnOfflineCheck()
        {
            using (var shutdown = new CancellationTokenSource())
            {
                var coordinator = new BundledRuntimeUpdateCoordinator(token =>
                {
                    shutdown.Cancel();
                    throw new OperationCanceledException(token);
                }, (info, token) => throw new AssertionException("Must not install."));
                Assert.CatchAsync<OperationCanceledException>(() => coordinator.EnsureAsync(CodexRuntimeSources.Bundled, shutdown.Token));
            }
        }

        private static CodexRuntimeUpdateInfo Update()
        {
            return new CodexRuntimeUpdateInfo { InstalledVersion = "0.162.0", LatestVersion = "0.162.1", TargetTriple = "x86_64-pc-windows-msvc" };
        }
    }
}
