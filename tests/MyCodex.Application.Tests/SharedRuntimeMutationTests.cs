using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SharedRuntimeMutationTests
    {
        private const string Triple = "x86_64-pc-windows-msvc";
        private string _fixture;
        private AppConfigService _paths;

        [SetUp]
        public void SetUp()
        {
            _fixture = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shared-runtime-mutation", Guid.NewGuid().ToString("N"));
            _paths = new AppConfigService(_fixture);
        }

        [Test]
        public async Task Reservation_SameRuntimeRootExcludesAnotherUpdaterAcrossThreadsAndTargetDataRoots()
        {
            var otherTarget = new AppConfigService(Path.Combine(_fixture, "other-target"), _fixture, "MyCodex-Dev");
            var first = Reserve();
            Assert.Throws<InvalidOperationException>(() => new SharedRuntimeMutationReservation(otherTarget.GetBundledCodexDirectory()));
            using (var independent = new SharedRuntimeMutationReservation(Path.Combine(_fixture, "other-installation", "runtime", "codex"))) { }
            await Task.Run(() => first.Dispose());
            using (var reopened = new SharedRuntimeMutationReservation(otherTarget.GetBundledCodexDirectory())) { }
        }

        [Test]
        public void PublishNewVersion_IsCompleteAndDoesNotReplaceRunningOldVersion()
        {
            var oldExe = Installed("0.123.0", "old");
            var package = Package("new");
            using (var reservation = new SharedRuntimeMutationReservation(_paths.GetBundledCodexDirectory(), () => new[] { oldExe }))
                Assert.That(reservation.PublishPackage(package, Destination("0.124.0"), Triple, false, CancellationToken.None), Is.True);
            Assert.That(File.ReadAllText(oldExe), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(_paths.GetBundledCodexExecutablePath("0.124.0", Triple)), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(Path.Combine(Destination("0.124.0"), "metadata.txt")), Is.EqualTo("complete"));
            Assert.That(_paths.GetLatestInstalledBundledCodexVersion(Triple), Is.EqualTo("0.124.0"));
        }

        [Test]
        public void PublishExistingCompleteWithoutForce_DoesNotRewriteIt()
        {
            var oldExe = Installed("0.124.0", "old");
            using (var reservation = new SharedRuntimeMutationReservation(_paths.GetBundledCodexDirectory(), () => { throw new InvalidOperationException(); }))
                Assert.That(reservation.PublishPackage(Package("new"), Destination("0.124.0"), Triple, false, CancellationToken.None), Is.False);
            Assert.That(File.ReadAllText(oldExe), Is.EqualTo("old"));
        }

        [Test]
        public void ForceReinstall_RunningVersionIsRejectedWithoutDeletingOldFiles()
        {
            var oldExe = Installed("0.124.0", "old");
            using (var reservation = new SharedRuntimeMutationReservation(_paths.GetBundledCodexDirectory(), () => new[] { oldExe.ToUpperInvariant() }))
                Assert.Throws<InvalidOperationException>(() => reservation.PublishPackage(Package("new"), Destination("0.124.0"), Triple, true, CancellationToken.None));
            Assert.That(File.ReadAllText(oldExe), Is.EqualTo("old"));
            Assert.That(Directory.GetDirectories(_paths.GetBundledCodexDirectory(), ".staging-*"), Is.Empty);
        }

        [Test]
        public void ForceReinstall_UnknownUseIsRejectedWithoutDeletingOldFiles()
        {
            var oldExe = Installed("0.124.0", "old");
            using (var reservation = new SharedRuntimeMutationReservation(_paths.GetBundledCodexDirectory(), () => { throw new UnauthorizedAccessException(); }))
                Assert.Throws<InvalidOperationException>(() => reservation.PublishPackage(Package("new"), Destination("0.124.0"), Triple, true, CancellationToken.None));
            Assert.That(File.ReadAllText(oldExe), Is.EqualTo("old"));
        }

        [Test]
        public void ForceReinstall_UnusedVersionReplacesWholePackage()
        {
            var oldExe = Installed("0.124.0", "old");
            File.WriteAllText(Path.Combine(Destination("0.124.0"), "obsolete.txt"), "obsolete");
            using (var reservation = Reserve())
                Assert.That(reservation.PublishPackage(Package("new"), Destination("0.124.0"), Triple, true, CancellationToken.None), Is.True);
            Assert.That(File.ReadAllText(oldExe), Is.EqualTo("new"));
            Assert.That(File.Exists(Path.Combine(Destination("0.124.0"), "obsolete.txt")), Is.False);
            Assert.That(Directory.GetDirectories(_paths.GetBundledCodexDirectory(), ".replaced-*"), Is.Empty);
            Assert.That(Directory.GetDirectories(_paths.GetBundledCodexDirectory(), ".execution-locks-*"), Is.Empty);
        }

        [Test]
        public void FailedDirectoryRename_RestoresReservedExecutablesAndRetainsOriginalPackage()
        {
            var oldExe = Installed("0.124.0", "old");
            var metadata = Path.Combine(Destination("0.124.0"), "metadata.txt");
            File.WriteAllText(metadata, "old-metadata");
            using (var locked = new FileStream(metadata, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var reservation = Reserve())
            {
                var error = Assert.Catch(() => reservation.PublishPackage(Package("new"), Destination("0.124.0"), Triple, true, CancellationToken.None));
                Assert.That(error, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            }
            Assert.That(File.ReadAllText(oldExe), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(metadata), Is.EqualTo("old-metadata"));
            Assert.That(Directory.GetDirectories(_paths.GetBundledCodexDirectory(), ".execution-locks-*"), Is.Empty);
            Assert.That(Directory.GetDirectories(_paths.GetBundledCodexDirectory(), ".staging-*"), Is.Empty);
        }

        [Test]
        public void FailedCopy_DoesNotExposePartialNewVersionToRuntimeSelection()
        {
            Installed("0.123.0", "old");
            var package = Package("new");
            using (var locked = new FileStream(Path.Combine(package, "metadata.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var reservation = Reserve())
                Assert.Throws<IOException>(() => reservation.PublishPackage(package, Destination("0.124.0"), Triple, false, CancellationToken.None));
            Assert.That(Directory.Exists(Destination("0.124.0")), Is.False);
            Assert.That(_paths.GetLatestInstalledBundledCodexVersion(Triple), Is.EqualTo("0.123.0"));
            Assert.That(Directory.GetDirectories(_paths.GetBundledCodexDirectory(), ".staging-*"), Is.Empty);
        }

        [Test]
        public void CancelledReplacement_LeavesOriginalVersionIntact()
        {
            var oldExe = Installed("0.124.0", "old");
            using (var reservation = Reserve())
                Assert.Throws<OperationCanceledException>(() => reservation.PublishPackage(Package("new"), Destination("0.124.0"), Triple, true, new CancellationToken(true)));
            Assert.That(File.ReadAllText(oldExe), Is.EqualTo("old"));
        }

        [Test]
        public void Cleanup_PreservesInUseVersionAndOnlyRemovesUnusedRequestedPlatform()
        {
            var used = Installed("0.122.0", "used");
            Installed("0.123.0", "unused");
            Installed("0.124.0", "keep");
            var otherPlatform = _paths.GetBundledCodexExecutablePath("0.123.0", "aarch64-pc-windows-msvc");
            Directory.CreateDirectory(Path.GetDirectoryName(otherPlatform));
            File.WriteAllText(otherPlatform, "other-platform");
            var service = new CodexRuntimeUpdateService(_paths, () => new[] { used });
            Assert.That(service.RemoveObsoleteInstalledVersions("0.124.0", Triple), Is.EqualTo(new[] { "0.123.0" }));
            Assert.That(File.ReadAllText(used), Is.EqualTo("used"));
            Assert.That(File.ReadAllText(otherPlatform), Is.EqualTo("other-platform"));
            Assert.That(Directory.Exists(Destination("0.123.0")), Is.False);
        }

        [Test]
        public void Cleanup_NeverRemovesAVersionNewerThanTheKeptRuntime()
        {
            Installed("0.123.0", "old");
            Installed("0.124.0", "keep");
            var newer = Installed("0.125.0", "newer");
            var service = new CodexRuntimeUpdateService(_paths, () => Enumerable.Empty<string>());
            Assert.That(service.RemoveObsoleteInstalledVersions("0.124.0", Triple), Is.EqualTo(new[] { "0.123.0" }));
            Assert.That(File.ReadAllText(newer), Is.EqualTo("newer"));
        }

        [Test]
        public void Cleanup_UnknownProcessInspectionLeavesVersionIntact()
        {
            var oldExe = Installed("0.123.0", "old");
            var service = new CodexRuntimeUpdateService(_paths, () => null);
            Assert.Throws<InvalidOperationException>(() => service.RemoveObsoleteInstalledVersions("0.124.0", Triple));
            Assert.That(File.ReadAllText(oldExe), Is.EqualTo("old"));
        }

        [Test]
        public void Cleanup_UsesExactDirectoryBoundaryAndRechecksBeforeMutation()
        {
            var oldExe = Installed("0.123.0", "old");
            var reads = 0;
            var service = new CodexRuntimeUpdateService(_paths, () => ++reads == 1
                ? new[] { oldExe.Replace("0.123.0", "0.123.0-other") } : new[] { oldExe });
            Assert.That(service.RemoveObsoleteInstalledVersions("0.124.0", Triple), Is.Empty);
            Assert.That(reads, Is.EqualTo(2));
            Assert.That(File.ReadAllText(oldExe), Is.EqualTo("old"));
        }

        [Test]
        public void PublishPackage_RejectsEscapedDestination()
        {
            using (var reservation = Reserve())
                Assert.Throws<InvalidOperationException>(() => reservation.PublishPackage(Package("new"), Path.Combine(_fixture, "outside"), Triple, true, CancellationToken.None));
            Assert.That(Directory.Exists(Path.Combine(_fixture, "outside")), Is.False);
        }

        private SharedRuntimeMutationReservation Reserve()
        {
            return new SharedRuntimeMutationReservation(_paths.GetBundledCodexDirectory(), () => Enumerable.Empty<string>());
        }

        private string Installed(string version, string content)
        {
            var executable = _paths.GetBundledCodexExecutablePath(version, Triple);
            Directory.CreateDirectory(Path.GetDirectoryName(executable));
            File.WriteAllText(executable, content);
            return executable;
        }

        private string Package(string content)
        {
            var package = Path.Combine(_fixture, "package");
            var executable = Path.Combine(package, "vendor", Triple, "bin", "codex.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable));
            File.WriteAllText(executable, content);
            File.WriteAllText(Path.Combine(package, "metadata.txt"), "complete");
            return package;
        }

        private string Destination(string version) { return _paths.GetBundledCodexInstallDirectory(version, Triple); }
    }
}
