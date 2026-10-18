using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Infrastructure.Updates;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class InstallerBuildScriptTests
    {
        [Test]
        public void BuildInstaller_UsesLatestInstalledBundledRuntimeBeforeOfficialLatestFallback()
        {
            var source = RepositorySource.ReadFile("tools", "installer", "build-installer.ps1");

            Assert.That(source, Does.Contain("function Get-LatestInstalledBundledCodexVersion"));
            Assert.That(source, Does.Contain("function Resolve-BundledCodexPackageVersion"));
            Assert.That(source, Does.Contain("$bundledCodexVersion = Resolve-BundledCodexPackageVersion"));
            Assert.That(source, Does.Contain("Using installed bundled Codex runtime"));
            Assert.That(source, Does.Contain("No installed bundled Codex runtime found"));
            Assert.That(source, Does.Contain("official latest bundled Codex runtime"));
            Assert.That(source, Does.Contain("Bundled Codex runtime $bundledCodexVersion $codexTargetTriple already exists."));
            Assert.That(source, Does.Not.Contain("$bundledCodexVersion = Get-BundledCodexVersion"));
        }

        [Test]
        public void BuildInstaller_CleansPackageRuntimeBeforeCompilingInstaller()
        {
            var source = RepositorySource.ReadFile("tools", "installer", "build-installer.ps1");
            var codexCleanupIndex = source.LastIndexOf("Remove-NonPinnedBundledCodexRuntimes", System.StringComparison.Ordinal);
            var compileIndex = source.IndexOf("Compiling installer with Inno Setup", System.StringComparison.Ordinal);

            Assert.That(codexCleanupIndex, Is.GreaterThan(0));
            Assert.That(compileIndex, Is.GreaterThan(codexCleanupIndex));
        }

        [Test]
        public void BuildInstaller_RemovesRetiredBundledNodeRuntimeBeforeCompilingInstaller()
        {
            var source = RepositorySource.ReadFile("tools", "installer", "build-installer.ps1");
            var cleanupIndex = source.LastIndexOf("Remove-RetiredBundledNodeRuntime -PackageSourceDir $sourceDirFull", System.StringComparison.Ordinal);
            var compileIndex = source.IndexOf("Compiling installer with Inno Setup", System.StringComparison.Ordinal);

            Assert.That(source, Does.Contain("function Remove-RetiredBundledNodeRuntime"));
            Assert.That(cleanupIndex, Is.GreaterThan(0));
            Assert.That(compileIndex, Is.GreaterThan(cleanupIndex));
        }

        [Test]
        public void BuildInstaller_ValidatesBundledCodexRuntimeBeforeCompilingInstaller()
        {
            var source = RepositorySource.ReadFile("tools", "installer", "build-installer.ps1");
            var compileIndex = source.IndexOf("Compiling installer with Inno Setup", System.StringComparison.Ordinal);

            Assert.That(source, Does.Contain("function Test-BundledCodexRuntimeExists"));
            Assert.That(source.LastIndexOf("Test-BundledCodexRuntimeExists", System.StringComparison.Ordinal), Is.LessThan(compileIndex));
            Assert.That(source, Does.Not.Contain("Test-BundledNodeRuntimeExists"));
        }

        [Test]
        public void BuildInstaller_BundlesRemoteServiceButNotProviderRuntime()
        {
            var source = RepositorySource.ReadFile("tools", "installer", "build-installer.ps1");

            Assert.That(source, Does.Not.Contain("mimo2codex"));
            Assert.That(source, Does.Contain("function Copy-BundledRelay"));
            Assert.That(source, Does.Contain(@"tools\runtime\prepare-relay-runtime.ps1"));
            Assert.That(source, Does.Contain(@"runtime\relay\mycodex-relay.exe"));
        }

        [Test]
        public void Installer_DeletesPreviouslyInstalledRuntimeDirectoryBeforeCopyingPackageFiles()
        {
            var source = RepositorySource.ReadFile("tools", "installer", "MyCodex.iss");

            Assert.That(source, Does.Contain("[InstallDelete]"));
            Assert.That(source, Does.Contain(@"Type: filesandordirs; Name: ""{app}\runtime\*"""));
            Assert.That(source, Does.Not.Contain(@"Type: filesandordirs; Name: ""{app}\runtime\codex\*"""));
            Assert.That(source, Does.Contain("Stop-MyCodexManagedProcesses.ps1"));
        }

        [Test]
        public void Installer_RequiresNormalResourceUserExitBeforeAuthenticationAndRuntimeReplacement()
        {
            var source = RepositorySource.ReadFile("tools", "installer", "MyCodex.iss");

            Assert.That(source, Does.Match(@"(?im)^CloseApplications=no\s*$"));
            Assert.That(source, Does.Not.Contain("CloseApplicationsFilter="));
            Assert.That(source, Does.Match(@"(?im)^RestartApplications=no\s*$"));
            var prepare = source.IndexOf("function PrepareToInstall", System.StringComparison.Ordinal);
            var check = source.IndexOf("Result := PrepareInstallationTargets();", prepare, System.StringComparison.Ordinal);
            var authentication = source.IndexOf("AuthenticationMaintenance('Prepare'", prepare, System.StringComparison.Ordinal);
            Assert.That(check, Is.GreaterThan(prepare));
            Assert.That(authentication, Is.GreaterThan(check));
            Assert.That(source, Does.Contain("if CurStep = ssInstall then"));
            Assert.That(source, Does.Contain("GuardError := ResourceGuardMessage(RunResourceGuard(' -IncludeManagedProcesses'))"));
            Assert.That(source, Does.Contain("RaiseException(GuardError)"));
            Assert.That(source, Does.Contain("ReleaseInstallationReservation()"));
        }

        [Test]
        public void OnlineUpdate_LaunchesStandaloneInstallerWithoutClientExitPrecondition()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.UpdatesTray.cs") +
                RepositorySource.ReadSourceFile("MainForm.SharedUpdates.cs");

            Assert.That(source, Does.Contain("await InstallDownloadedUpdateAsync(download.InstallerPath)"));
            Assert.That(source, Does.Contain("Process.Start(AppUpdateService.CreateInstallerStartInfo("));
            Assert.That(source, Does.Not.Contain("RunAllTargetExitAsync"));
            Assert.That(source, Does.Not.Contain("DeferredUpdateInstallerLauncher.ScheduleAsync"));
            Assert.That(source, Does.Not.Contain("TryReserveSharedUpdate"));
            Assert.That(source, Does.Not.Contain("DiscoverInstallationUsers"));
            Assert.That(source, Does.Not.Contain("FileName = installerPath"));
            Assert.That(source, Does.Not.Contain("Wait-Process -Id"));
            Assert.That(source, Does.Not.Contain("Application.Exit"));
            Assert.That(source, Does.Not.Contain("StartUpdateInstallerAndExit"));
        }

        [Test]
        public void OnlineUpdate_HandsExactInstallationToInteractiveSetupWithoutForcedExit()
        {
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "installer launch " + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var installer = Path.Combine(directory, "download.exe");
                File.WriteAllText(installer, "fixture; never executed");
                var start = AppUpdateService.CreateInstallerStartInfo(installer, directory + Path.DirectorySeparatorChar);
                Assert.That(start.FileName, Is.EqualTo(installer));
                Assert.That(start.Arguments, Is.EqualTo("/SP- /DIR=\"" + directory + "\""));
                Assert.That(start.UseShellExecute, Is.True);
                Assert.That(start.CreateNoWindow, Is.False);
                File.Delete(installer);
                Assert.Throws<System.InvalidOperationException>(() => AppUpdateService.CreateInstallerStartInfo(installer, directory));
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
