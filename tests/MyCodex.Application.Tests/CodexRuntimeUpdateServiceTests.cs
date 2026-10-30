using System;
using System.IO;
using System.Reflection;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class CodexRuntimeUpdateServiceTests
    {
        [Test]
        public void CheckedInstallRejectsAnIncompleteSnapshotWithoutFetchingAgain()
        {
            var root = CreateTempDirectory();
            try
            {
                var packages = new CodexRuntimePackageClient((url, token) =>
                    throw new AssertionException("A checked install must not query latest again."));
                var service = new CodexRuntimeUpdateService(new AppConfigService(root), packages);
                Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallCheckedAsync(
                    new CodexRuntimeUpdateInfo { LatestVersion = "0.124.0", TargetTriple = "x86_64-pc-windows-msvc" },
                    null, null, System.Threading.CancellationToken.None));
            }
            finally { DeleteDirectory(root); }
        }

        [TestCase("0.124.0")]
        [TestCase("0.125.0")]
        public async System.Threading.Tasks.Task CheckedInstallReusesAPackagePublishedByAnotherWindowWithoutNetwork(string installed)
        {
            var root = CreateTempDirectory();
            try
            {
                var paths = new AppConfigService(root);
                const string target = "x86_64-pc-windows-msvc";
                CreateBundledExecutable(paths, installed, target);
                var service = new CodexRuntimeUpdateService(paths, () => new string[0]);
                var result = await service.InstallCheckedAsync(new CodexRuntimeUpdateInfo
                {
                    InstalledVersion = "0.123.0", LatestVersion = "0.124.0", TargetTriple = target
                }, null, null, System.Threading.CancellationToken.None);
                Assert.That(result.WasUpdated, Is.False);
                Assert.That(result.Version, Is.EqualTo(installed));
                Assert.That(File.Exists(result.ExecutablePath), Is.True);
            }
            finally { DeleteDirectory(root); }
        }

        [Test]
        public void RemoveObsoleteInstalledVersions_DeletesOlderVersionForTargetTriple()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var appConfigService = new AppConfigService(rootPath);
                var updateService = new CodexRuntimeUpdateService(appConfigService);
                var targetTriple = "x86_64-pc-windows-msvc";

                CreateBundledExecutable(appConfigService, "0.123.0", targetTriple);
                CreateBundledExecutable(appConfigService, "0.124.0", targetTriple);

                var removedVersions = updateService.RemoveObsoleteInstalledVersions("0.124.0", targetTriple);

                Assert.That(removedVersions, Is.EquivalentTo(new[] { "0.123.0" }));
                Assert.That(Directory.Exists(appConfigService.GetBundledCodexVersionDirectory("0.123.0")), Is.False);
                Assert.That(File.Exists(appConfigService.GetBundledCodexExecutablePath("0.124.0", targetTriple)), Is.True);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void RemoveObsoleteInstalledVersions_SkipsUnsafeInstalledVersionDirectories()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var appConfigService = new AppConfigService(rootPath);
                var updateService = new CodexRuntimeUpdateService(appConfigService);
                var targetTriple = "x86_64-pc-windows-msvc";

                Directory.CreateDirectory(Path.Combine(appConfigService.GetBundledCodexDirectory(), "bad version!"));
                CreateBundledExecutable(appConfigService, "0.123.0", targetTriple);
                CreateBundledExecutable(appConfigService, "0.124.0", targetTriple);

                var removedVersions = updateService.RemoveObsoleteInstalledVersions("0.124.0", targetTriple);

                Assert.That(removedVersions, Is.EquivalentTo(new[] { "0.123.0" }));
                Assert.That(Directory.Exists(Path.Combine(appConfigService.GetBundledCodexDirectory(), "bad version!")), Is.True);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [TestCase("package/bin/codex", true)]
        [TestCase("package/lib/file with space.txt", true)]
        [TestCase("package/", true)]
        [TestCase("../evil", false)]
        [TestCase("package/../../evil", false)]
        [TestCase("/tmp/evil", false)]
        [TestCase("\\tmp\\evil", false)]
        [TestCase("C:/tmp/evil", false)]
        [TestCase("C:\\tmp\\evil", false)]
        [TestCase("", false)]
        public void IsSafeArchiveEntryName_ValidatesTarEntryNames(string entryName, bool expected)
        {
            MethodInfo method = typeof(CodexRuntimeUpdateService).GetMethod(
                "IsSafeArchiveEntryName",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            bool actual = (bool)method.Invoke(null, new object[] { entryName });

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void EnsureSafeArchiveListing_RejectsUnsafeEntry()
        {
            MethodInfo method = typeof(CodexRuntimeUpdateService).GetMethod(
                "EnsureSafeArchiveListing",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                delegate { method.Invoke(null, new object[] { "package/bin/codex\n../evil\n" }); });

            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [TestCase("../0.124.0")]
        [TestCase("0.124.0\\evil")]
        [TestCase("C:\\temp\\0.124.0")]
        [TestCase(".")]
        [TestCase("..")]
        public void RemoveObsoleteInstalledVersions_RejectsUnsafeKeepVersion(string keepVersion)
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var updateService = new CodexRuntimeUpdateService(new AppConfigService(rootPath));

                Assert.That(
                    () => updateService.RemoveObsoleteInstalledVersions(keepVersion, "x86_64-pc-windows-msvc"),
                    Throws.ArgumentException);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [TestCase("../x86_64-pc-windows-msvc")]
        [TestCase("x86_64-pc-windows-msvc\\evil")]
        [TestCase("C:\\temp\\x86_64-pc-windows-msvc")]
        [TestCase(".")]
        [TestCase("..")]
        public void RemoveObsoleteInstalledVersions_RejectsUnsafeTargetTriple(string targetTriple)
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var updateService = new CodexRuntimeUpdateService(new AppConfigService(rootPath));

                Assert.That(
                    () => updateService.RemoveObsoleteInstalledVersions("0.124.0", targetTriple),
                    Throws.ArgumentException);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void ResolvePackageCodexExecutablePath_WhenCurrentLayoutExists_ReturnsBinExecutable()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var targetTriple = "x86_64-pc-windows-msvc";
                var executablePath = Path.Combine(rootPath, "vendor", targetTriple, "bin", "codex.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(executablePath));
                File.WriteAllText(executablePath, string.Empty);

                var resolved = InvokeResolvePackageCodexExecutablePath(rootPath, targetTriple);

                Assert.That(resolved, Is.EqualTo(executablePath));
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void ResolvePackageCodexExecutablePath_WhenOnlyLegacyLayoutExists_ReturnsLegacyExecutable()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var targetTriple = "x86_64-pc-windows-msvc";
                var executablePath = Path.Combine(rootPath, "vendor", targetTriple, "codex", "codex.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(executablePath));
                File.WriteAllText(executablePath, string.Empty);

                var resolved = InvokeResolvePackageCodexExecutablePath(rootPath, targetTriple);

                Assert.That(resolved, Is.EqualTo(executablePath));
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void ResolveTarExecutablePath_UsesSystemDirectoryWhenAvailable()
        {
            var systemTar = Path.Combine(Environment.SystemDirectory, "tar.exe");

            Assert.That(
                CodexRuntimeUpdateService.ResolveTarExecutablePath(),
                Is.EqualTo(File.Exists(systemTar) ? systemTar : "tar.exe"));
        }

        private static void CreateBundledExecutable(AppConfigService service, string version, string targetTriple)
        {
            var executablePath = service.GetBundledCodexExecutablePath(version, targetTriple);
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath));
            File.WriteAllText(executablePath, string.Empty);
        }

        private static string InvokeResolvePackageCodexExecutablePath(string packageRoot, string targetTriple)
        {
            MethodInfo method = typeof(CodexRuntimeUpdateService).GetMethod(
                "ResolvePackageCodexExecutablePath",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (string)method.Invoke(null, new object[] { packageRoot, targetTriple });
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "MyCodex-CodexRuntimeUpdateTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
