using System;
using System.IO;
using System.Linq;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.Assets)]
    [TestFixture]
    public sealed class InstallerScriptTests
    {
        [TestCase("build.bat")]
        [TestCase("build-dev.bat")]
        [TestCase("package.bat")]
        public void BuildScript_UsesCrLfSoCmdCanResolveLabels(string fileName)
        {
            var bytes = File.ReadAllBytes(RepositorySource.GetPath(fileName));
            for (var index = 0; index < bytes.Length; index++)
            {
                if (bytes[index] == 10)
                {
                    Assert.That(index, Is.GreaterThan(0));
                    Assert.That(bytes[index - 1], Is.EqualTo(13), fileName + " contains an LF-only line ending.");
                }
            }
        }

        [Test]
        public void MyCodexInstaller_RemembersPreviousInstallDirectoryForUpdates()
        {
            var script = RepositorySource.ReadFile("tools", "installer", "MyCodex.iss");

            Assert.That(script, Does.Match(@"(?im)^AppId=\{\{[0-9A-F-]+\}\s*$"));
            Assert.That(script, Does.Match(@"(?im)^UsePreviousAppDir=yes\s*$"));
            Assert.That(script, Does.Not.Match(@"(?im)^UsePreviousAppDir=no\s*$"));
        }

        [Test]
        public void MyCodexInstaller_SelectsDesktopShortcutByDefault()
        {
            var script = RepositorySource.ReadFile("tools", "installer", "MyCodex.iss");

            var taskLine = script
                .Replace("\r\n", "\n")
                .Split(new[] { '\n' }, StringSplitOptions.None)
                .FirstOrDefault(line => line.StartsWith("Name: \"desktopicon\";", StringComparison.OrdinalIgnoreCase));

            Assert.That(taskLine, Is.Not.Null);
            Assert.That(taskLine, Does.Not.Contain("unchecked"));
        }

        [TestCase("build.bat", "Release")]
        [TestCase("build-dev.bat", "Dev")]
        public void BuildScript_DelegatesToSharedPowerShellEntryPoint(string fileName, string profile)
        {
            var buildScript = NormalizeLineEndings(RepositorySource.ReadFile(fileName));

            Assert.That(buildScript, Does.Not.Contain("--enable-remote-service"));
            Assert.That(buildScript, Does.Not.Contain("--disable-remote-service"));
            Assert.That(buildScript, Does.Contain(@"tools\build\Build-MyCodex.ps1"));
            Assert.That(buildScript, Does.Contain("-Profile " + profile));
            Assert.That(buildScript, Does.Not.Contain("dotnet build"));
            Assert.That(buildScript, Does.Not.Contain("prepare-node-runtime.ps1"));
            Assert.That(buildScript, Does.Not.Contain("CopyBundledRelay"));
        }

        [Test]
        public void SharedBuild_UsesOfficialCodexRuntimeWithoutProviderRuntime()
        {
            var sharedBuild = RepositorySource.ReadFile("tools", "build", "Build-MyCodex.ps1");

            Assert.That(sharedBuild, Does.Not.Contain("mimo2codex"));
            Assert.That(sharedBuild, Does.Contain("Copy-BundledRelay -OutputDirectory $outputDirectory"));
            Assert.That(sharedBuild, Does.Contain("prepare-relay-runtime.ps1"));
            Assert.That(sharedBuild, Does.Not.Contain("MyCodex-relay"));
            Assert.That(sharedBuild, Does.Not.Contain("build_all.bat"));
            Assert.That(sharedBuild, Does.Contain("runtime\\relay"));
            Assert.That(sharedBuild, Does.Contain("MyCodexChannel=Dev"));
            Assert.That(sharedBuild, Does.Contain("MyCodex.Dev.exe"));
            Assert.That(sharedBuild, Does.Contain("$configuration = \"Debug\""));
            Assert.That(sharedBuild, Does.Contain("$configuration = \"Release\""));
            Assert.That(sharedBuild, Does.Contain("Join-Path $repoRoot \"debug\""));
            Assert.That(sharedBuild, Does.Contain("Join-Path $repoRoot \"Release\""));
            Assert.That(sharedBuild, Does.Contain("prepare-bundled-codex-runtime.ps1"));
            Assert.That(sharedBuild, Does.Not.Contain("prepare-node-runtime.ps1"));
        }

        [Test]
        public void LocalBuild_AllowsModifiedBackendWhileInstallerKeepsReleaseValidation()
        {
            var sharedBuild = RepositorySource.ReadFile("tools", "build", "Build-MyCodex.ps1");
            var installerBuild = RepositorySource.ReadFile("tools", "installer", "build-installer.ps1");

            Assert.That(sharedBuild, Does.Contain("-AllowDevelopment | Out-Null"));
            Assert.That(sharedBuild, Does.Not.Contain("-AllowDevelopment:($Profile"));
            Assert.That(installerBuild, Does.Contain("-AllowDevelopment:$AllowDevelopmentAuthenticationHost"));
            Assert.That(installerBuild, Does.Contain("$AllowDevelopmentAuthenticationHost -and $Configuration -ne 'Debug'"));
        }

        [Test]
        public void InstallerBuild_UsesPinnedRelayPreparationWithoutSiblingRepository()
        {
            var installerBuild = RepositorySource.ReadFile("tools", "installer", "build-installer.ps1");

            Assert.That(installerBuild, Does.Contain("prepare-relay-runtime.ps1"));
            Assert.That(installerBuild, Does.Not.Contain("MyCodex-relay"));
            Assert.That(installerBuild, Does.Not.Contain("build_all.bat"));
        }

        [Test]
        public void PackageBuild_BuildsReleaseOnceAndPackagesThatOutput()
        {
            var packageBuild = NormalizeLineEndings(RepositorySource.ReadFile("package.bat"));

            Assert.That(packageBuild, Does.Not.Contain("--enable-remote-service"));
            Assert.That(packageBuild, Does.Not.Contain("--disable-remote-service"));
            Assert.That(packageBuild, Does.Not.Contain("EnableRemoteService"));
            Assert.That(packageBuild, Does.Not.Contain("DisableRemoteService"));
            Assert.That(packageBuild, Does.Not.Contain("MYCODEX_REMOTE_SERVICE_ENABLED"));
            Assert.That(packageBuild, Does.Contain("Release\\MyCodex.exe is in use. Close MyCodex and retry."));
            Assert.That(packageBuild, Does.Contain("dotnet build"));
            Assert.That(packageBuild, Does.Contain(@"src\MyCodex.WinForms\MyCodex.WinForms.csproj"));
            Assert.That(packageBuild, Does.Contain("-c Release"));
            Assert.That(packageBuild, Does.Contain("powershell -NoProfile -ExecutionPolicy Bypass -File"));
            Assert.That(packageBuild, Does.Contain(@"-SourceDir ""%ROOT%Release"" -SkipBuild"));
            Assert.That(packageBuild, Does.Not.Contain("prepare-bundled-codex-runtime.ps1"));
            Assert.That(packageBuild, Does.Not.Contain("prepare-node-runtime.ps1"));
            Assert.That(packageBuild, Does.Not.Contain("prepare-mimo2codex-runtime.ps1"));
            Assert.That(packageBuild, Does.Contain("%*"));

            var releaseLockCheckIndex = packageBuild.IndexOf("Release\\MyCodex.exe is in use", StringComparison.Ordinal);
            var releaseBuildIndex = packageBuild.IndexOf("dotnet build", StringComparison.Ordinal);
            var installerBuildIndex = packageBuild.IndexOf("build-installer.ps1", StringComparison.Ordinal);
            Assert.That(releaseLockCheckIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(releaseBuildIndex, Is.GreaterThan(releaseLockCheckIndex));
            Assert.That(installerBuildIndex, Is.GreaterThan(releaseBuildIndex));
        }

        [Test]
        public void ReleaseDistributionScripts_UseBoundaryAwareSafeDelete()
        {
            var installerBuild = RepositorySource.ReadFile("tools", "installer", "build-installer.ps1");
            var gitCodeDistribution = RepositorySource.ReadFile("tools", "gitcode", "Publish-GitCodeDistributionRepo.ps1");

            AssertBoundaryAwareSafeDelete(installerBuild);
            AssertBoundaryAwareSafeDelete(gitCodeDistribution);
        }

        [Test]
        public void InstallerBuild_PreparesAdaptiveBundledCodexRuntime()
        {
            var installerBuild = RepositorySource.ReadFile("tools", "installer", "build-installer.ps1");

            Assert.That(installerBuild, Does.Contain("[string]$BundledCodexVersion"));
            Assert.That(installerBuild, Does.Contain("function Get-LatestInstalledBundledCodexVersion"));
            Assert.That(installerBuild, Does.Contain("function Resolve-BundledCodexPackageVersion"));
            Assert.That(installerBuild, Does.Contain("Using installed bundled Codex runtime"));
            Assert.That(installerBuild, Does.Contain("official latest bundled Codex runtime"));
            Assert.That(installerBuild, Does.Contain("prepare-bundled-codex-runtime.ps1"));
            Assert.That(installerBuild, Does.Contain("-Version $bundledCodexVersion"));
            Assert.That(installerBuild, Does.Contain("runtime\\codex"));
            Assert.That(installerBuild, Does.Not.Contain("CodexRuntimeSources.cs"));
            Assert.That(installerBuild, Does.Not.Contain("BundledDefaultVersion"));
        }

        [Test]
        public void BundledCodexRuntime_DefaultVersionIsCurrentPinnedPackageVersion()
        {
            var source = RepositorySource.ReadSourceFile("CodexRuntimeSources.cs");

            Assert.That(source, Does.Contain("BundledDefaultVersion = \"0.147.0\""));
        }

        [Test]
        public void InstallerBuild_RemovesNonPinnedBundledCodexRuntimeVersionsFromPackageSource()
        {
            var installerBuild = RepositorySource.ReadFile("tools", "installer", "build-installer.ps1");

            Assert.That(installerBuild, Does.Contain("function Remove-NonPinnedBundledCodexRuntimes"));
            Assert.That(installerBuild, Does.Contain("Remove-NonPinnedBundledCodexRuntimes"));
            Assert.That(installerBuild, Does.Contain("-RequiredVersion $bundledCodexVersion"));
            Assert.That(installerBuild, Does.Contain("Removing non-pinned bundled Codex runtime"));
        }

        [Test]
        public void MyCodexInstaller_RemovesInstalledRuntimeDirectoryBeforeCopyingPackagedRuntimes()
        {
            var script = RepositorySource.ReadFile("tools", "installer", "MyCodex.iss");

            Assert.That(script, Does.Contain(@"Name: ""{app}\runtime\*"""));
            Assert.That(script, Does.Not.Contain(@"Name: ""{app}\runtime\codex\*"""));
            Assert.That(script, Does.Contain("Stop-MyCodexManagedProcesses.ps1"));
            Assert.That(script, Does.Not.Contain("runtime\\gateways"));
        }

        private static string NormalizeLineEndings(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
        }

        private static void AssertBoundaryAwareSafeDelete(string script)
        {
            Assert.That(script, Does.Contain("function Test-IsPathUnderRoot"));
            Assert.That(script, Does.Contain("$rootWithSeparator"));
            Assert.That(script, Does.Contain("DirectorySeparatorChar"));
            Assert.That(script, Does.Contain("$resolvedPath -ne $resolvedRoot"));
            Assert.That(script, Does.Contain("$resolvedPath.StartsWith($rootWithSeparator"));
            Assert.That(script, Does.Not.Contain("$resolvedPath.StartsWith($resolvedRoot"));
        }
    }
}
