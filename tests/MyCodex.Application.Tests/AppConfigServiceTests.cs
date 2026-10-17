using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Constants;
using NUnit.Framework;
using MyCodex.Infrastructure.Persistence;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class AppConfigServiceTests
    {
        [Test]
        public void EnsureConfigDirectory_CreatesExpectedSubdirectoriesUnderOverrideRoot()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(rootPath);

                service.EnsureConfigDirectory();

                Assert.That(Directory.Exists(rootPath), Is.True);
                Assert.That(Directory.Exists(service.GetConfigDirectory()), Is.True);
                Assert.That(Directory.Exists(service.GetLogDirectory()), Is.True);
                Assert.That(Directory.Exists(service.GetAttachmentTempDirectory()), Is.True);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void BundledCodexPaths_AreComposedFromOverrideRoot()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(rootPath);

                var versionDirectory = service.GetBundledCodexVersionDirectory("1.2.3");
                var installDirectory = service.GetBundledCodexInstallDirectory("1.2.3", "x86_64-pc-windows-msvc");
                var executablePath = service.GetBundledCodexExecutablePath("1.2.3", "x86_64-pc-windows-msvc");
                var pathDirectory = service.GetBundledCodexPathDirectory("1.2.3", "x86_64-pc-windows-msvc");

                Assert.That(versionDirectory, Is.EqualTo(Path.Combine(rootPath, "runtime", "codex", "1.2.3")));
                Assert.That(installDirectory, Is.EqualTo(Path.Combine(rootPath, "runtime", "codex", "1.2.3", "x86_64-pc-windows-msvc")));
                Assert.That(executablePath, Is.EqualTo(Path.Combine(rootPath, "runtime", "codex", "1.2.3", "x86_64-pc-windows-msvc", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe")));
                Assert.That(pathDirectory, Is.EqualTo(Path.Combine(rootPath, "runtime", "codex", "1.2.3", "x86_64-pc-windows-msvc", "vendor", "x86_64-pc-windows-msvc", "codex-path")));
                Assert.That(service.HasSeparateDataRoot, Is.False);
                Assert.That(service.GetInstallationDirectory(), Is.EqualTo(rootPath));
                Assert.That(service.GetSharedDataDirectory(), Is.EqualTo(rootPath));
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void SeparateDataRoot_LeavesRuntimeAndSharedServiceDataAtTheirOriginalRoots()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var targetRoot = Path.Combine(rootPath, "channel", "targets", "target-a");
                var installationRoot = Path.Combine(rootPath, "installation");
                var sharedRoot = Path.Combine(rootPath, "channel");
                var service = new AppConfigService(targetRoot, installationRoot, "MyCodex-Dev", sharedRoot);

                service.EnsureConfigDirectory();

                Assert.That(service.HasSeparateDataRoot, Is.True);
                Assert.That(service.GetAppDataDirectory(), Is.EqualTo(targetRoot));
                Assert.That(service.GetSharedDataDirectory(), Is.EqualTo(sharedRoot));
                Assert.That(service.GetInstallationDirectory(), Is.EqualTo(installationRoot));
                Assert.That(service.GetRuntimeDirectory(), Is.EqualTo(Path.Combine(installationRoot, "runtime")));
                Assert.That(service.GetConfigFilePath(), Is.EqualTo(Path.Combine(targetRoot, "config", "config.json")));
                Assert.That(service.GetAppStateFilePath(), Is.EqualTo(Path.Combine(targetRoot, "config", "app-state.json")));
                Assert.That(service.GetModeSettingsFilePath(), Is.EqualTo(Path.Combine(targetRoot, "config", "mode-settings.json")));
                Assert.That(service.GetRemoteServiceConfigFilePath(), Is.EqualTo(Path.Combine(targetRoot, "config", "remote-service.json")));
                Assert.That(service.GetRemoteBindingsFilePath(), Is.EqualTo(Path.Combine(targetRoot, "config", "remote-bindings.json")));
                Assert.That(service.GetLogDirectory(), Is.EqualTo(Path.Combine(targetRoot, "logs")));
                Assert.That(service.GetAttachmentTempDirectory(), Is.EqualTo(Path.Combine(targetRoot, "temp", "attachments")));
                Assert.That(service.GetUpdateTempDirectory(), Is.EqualTo(Path.Combine(targetRoot, "temp", "updates")));
                Assert.That(service.GetAutomationsFilePath(), Is.EqualTo(Path.Combine(targetRoot, "config", "automations.json")));
                Assert.That(service.GetAutomationWorkspaceDirectory(), Is.EqualTo(Path.Combine(targetRoot, "automations", "workspace")));
                Assert.That(service.GetRuntimeAppStateFilePath(), Is.EqualTo("~/.codex/mycodex-dev/app-state.json"));
                Assert.That(Directory.Exists(installationRoot), Is.False);
                Assert.That(Directory.Exists(Path.Combine(targetRoot, "runtime")), Is.False);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void SeparateDataRoot_WithNoSharedDataOverride_UsesTargetRootForSharedData()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(rootPath, AppDomain.CurrentDomain.BaseDirectory, "MyCodex");

                Assert.That(service.GetSharedDataDirectory(), Is.EqualTo(rootPath));
                Assert.That(service.HasSeparateDataRoot, Is.True);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void SeparateDataRoot_RequiresExplicitTargetAndInstallationRoots(string emptyRoot)
        {
            Assert.That(() => new AppConfigService(emptyRoot, AppDomain.CurrentDomain.BaseDirectory, "MyCodex"),
                Throws.ArgumentException);
            Assert.That(() => new AppConfigService(AppDomain.CurrentDomain.BaseDirectory, emptyRoot, "MyCodex"),
                Throws.ArgumentException);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LegacyMigration_OnlyLegacyConstructionImportsOldConfiguration(bool useSeparateRoot)
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var legacyRoot = Path.Combine(rootPath, "legacy");
                Directory.CreateDirectory(legacyRoot);
                File.WriteAllText(Path.Combine(legacyRoot, "config.json"), "legacy settings");
                File.WriteAllText(Path.Combine(legacyRoot, "app-state.json"), "legacy state");
                File.WriteAllText(Path.Combine(legacyRoot, "remote-service.json"), "legacy identity");
                File.WriteAllText(Path.Combine(legacyRoot, "remote-bindings.json"), "legacy bindings");
                var targetRoot = Path.Combine(rootPath, "target");
                var service = useSeparateRoot
                    ? new AppConfigService(targetRoot, AppDomain.CurrentDomain.BaseDirectory, "MyCodex")
                    : new AppConfigService(targetRoot);

                service.MigrateLegacyConfigFiles(legacyRoot);

                Assert.That(File.Exists(service.GetConfigFilePath()), Is.EqualTo(!useSeparateRoot));
                Assert.That(File.Exists(service.GetAppStateFilePath()), Is.EqualTo(!useSeparateRoot));
                Assert.That(File.Exists(service.GetRemoteServiceConfigFilePath()), Is.False);
                Assert.That(File.Exists(service.GetRemoteBindingsFilePath()), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(legacyRoot, "config.json")), Is.EqualTo("legacy settings"));
                if (!useSeparateRoot)
                {
                    File.WriteAllText(service.GetConfigFilePath(), "current settings");
                    service.MigrateLegacyConfigFiles(legacyRoot);
                    Assert.That(File.ReadAllText(service.GetConfigFilePath()), Is.EqualTo("current settings"));
                }
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [TestCase("..\\evil")]
        [TestCase("../evil")]
        [TestCase("C:\\temp\\evil")]
        [TestCase("/tmp/evil")]
        [TestCase("1.2.3\\evil")]
        [TestCase("1.2.3/evil")]
        [TestCase(".")]
        [TestCase("..")]
        public void BundledCodexPaths_RejectUnsafeVersionSegments(string version)
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(rootPath);

                Assert.That(
                    () => service.GetBundledCodexVersionDirectory(version),
                    Throws.ArgumentException);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [TestCase("..\\evil")]
        [TestCase("../evil")]
        [TestCase("C:\\temp\\evil")]
        [TestCase("/tmp/evil")]
        [TestCase("x86_64-pc-windows-msvc\\evil")]
        [TestCase(".")]
        [TestCase("..")]
        public void BundledCodexPaths_RejectUnsafeTargetTripleSegments(string targetTriple)
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(rootPath);

                Assert.That(
                    () => service.GetBundledCodexInstallDirectory("1.2.3", targetTriple),
                    Throws.ArgumentException);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void BundledCodexPaths_WhenLegacyLayoutExists_ReturnLegacyPaths()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(rootPath);
                var targetTriple = "x86_64-pc-windows-msvc";
                var executablePath = GetLegacyBundledExecutablePath(service, "1.2.3", targetTriple);
                var pathDirectory = Path.Combine(service.GetBundledCodexInstallDirectory("1.2.3", targetTriple), "vendor", targetTriple, "path");
                Directory.CreateDirectory(Path.GetDirectoryName(executablePath));
                Directory.CreateDirectory(pathDirectory);
                File.WriteAllText(executablePath, string.Empty);

                Assert.That(service.GetBundledCodexExecutablePath("1.2.3", targetTriple), Is.EqualTo(executablePath));
                Assert.That(service.GetBundledCodexPathDirectory("1.2.3", targetTriple), Is.EqualTo(pathDirectory));
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void RuntimeDirectory_WithoutOverride_IsUnderApplicationBaseDirectory()
        {
            var service = new AppConfigService();

            Assert.That(service.GetRuntimeDirectory(), Is.EqualTo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime")));
        }

        [Test]
        public void TempDirectory_WithoutOverride_IsChannelScoped()
        {
            var stable = new AppConfigService(appDirectoryName: "MyCodex");
            var dev = new AppConfigService(appDirectoryName: "MyCodex-Dev");

            Assert.That(stable.GetTempDirectory(), Is.Not.EqualTo(dev.GetTempDirectory()));
            Assert.That(stable.GetTempDirectory(), Does.EndWith(Path.Combine("MyCodex", "temp")));
            Assert.That(dev.GetTempDirectory(), Does.EndWith(Path.Combine("MyCodex-Dev", "temp")));
        }

        [Test]
        public void RemoteBindingsFilePath_IsChannelScoped()
        {
            var stable = new AppConfigService(appDirectoryName: "MyCodex");
            var dev = new AppConfigService(appDirectoryName: "MyCodex-Dev");

            Assert.That(stable.GetRemoteBindingsFilePath(), Is.Not.EqualTo(dev.GetRemoteBindingsFilePath()));
            Assert.That(
                stable.GetRemoteBindingsFilePath(),
                Does.EndWith(Path.Combine("MyCodex", "config", "remote-bindings.json")));
            Assert.That(
                dev.GetRemoteBindingsFilePath(),
                Does.EndWith(Path.Combine("MyCodex-Dev", "config", "remote-bindings.json")));
        }

        [Test]
        public void RuntimeAppStatePath_IsChannelScoped()
        {
            var stable = new AppConfigService(appDirectoryName: "MyCodex");
            var dev = new AppConfigService(appDirectoryName: "MyCodex-Dev");

            Assert.That(stable.GetRuntimeAppStateFilePath(), Is.EqualTo("~/.codex/mycodex/app-state.json"));
            Assert.That(dev.GetRuntimeAppStateFilePath(), Is.EqualTo("~/.codex/mycodex-dev/app-state.json"));
        }

        [Test]
        public void GetLatestInstalledBundledCodexVersion_ReturnsHighestVersionWithExecutable()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(rootPath);
                var targetTriple = "x86_64-pc-windows-msvc";
                CreateBundledExecutable(service, "1.2.3", targetTriple);
                CreateBundledExecutable(service, "1.2.5", targetTriple);
                CreateLegacyBundledExecutable(service, "1.2.4", targetTriple);
                Directory.CreateDirectory(service.GetBundledCodexInstallDirectory("2.0.0", targetTriple));

                var version = service.GetLatestInstalledBundledCodexVersion(targetTriple);

                Assert.That(version, Is.EqualTo("1.2.5"));
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void GetLatestInstalledBundledCodexVersion_SkipsUnsafeVersionDirectories()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(rootPath);
                var targetTriple = "x86_64-pc-windows-msvc";
                Directory.CreateDirectory(Path.Combine(service.GetBundledCodexDirectory(), "bad version!"));
                CreateBundledExecutable(service, "1.2.5", targetTriple);

                var version = service.GetLatestInstalledBundledCodexVersion(targetTriple);

                Assert.That(version, Is.EqualTo("1.2.5"));
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void PluginMarketContracts_ExposeMinimalShape()
        {
            var entry = new PluginMarketEntrySnapshot
            {
                MarketId = "mycodex-official",
                PluginId = "sample-plugin",
                DisplayName = "Sample Plugin",
                Version = "1.0.0",
                Category = "tools",
                Summary = "Minimal summary",
                PackageUrl = "https://example.invalid/plugins/sample-plugin.zip",
                ManifestUrl = "https://example.invalid/plugins/sample-plugin/manifest.json",
                DetailsUrl = "https://example.invalid/plugins/sample-plugin",
                AuthPolicy = "optional"
            };
            var market = new PluginMarketSnapshot
            {
                MarketId = "mycodex-official",
                DisplayName = "MyCodex Official",
                ManifestUrl = "https://example.invalid/marketplace.json",
                Plugins = new List<PluginMarketEntrySnapshot> { entry }
            };
            var catalog = new PluginMarketCatalogSnapshot
            {
                IndexUrl = "https://market.mycodex.app/catalog/index.json",
                Markets = new List<PluginMarketSnapshot> { market }
            };
            Assert.That(catalog.Markets, Has.Count.EqualTo(1));
            Assert.That(catalog.Markets[0].Plugins, Has.Count.EqualTo(1));
            Assert.That(catalog.Markets[0].Plugins[0].PackageUrl, Is.EqualTo("https://example.invalid/plugins/sample-plugin.zip"));

            Func<IPluginMarketCatalogClient, CancellationToken, Task<PluginMarketCatalogSnapshot>> loadIndex = (client, token) => client.LoadIndexAsync(token);
            Func<IPluginMarketCatalogClient, string, CancellationToken, Task<PluginMarketSnapshot>> loadMarket = (client, url, token) => client.LoadMarketAsync(url, token);

            Assert.That(loadIndex, Is.Not.Null);
            Assert.That(loadMarket, Is.Not.Null);
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "MyCodex-AppConfigTests-" + Guid.NewGuid().ToString("N"));
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

        private static void CreateBundledExecutable(AppConfigService service, string version, string targetTriple)
        {
            var executablePath = service.GetBundledCodexExecutablePath(version, targetTriple);
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath));
            File.WriteAllText(executablePath, string.Empty);
        }

        private static void CreateLegacyBundledExecutable(AppConfigService service, string version, string targetTriple)
        {
            var executablePath = GetLegacyBundledExecutablePath(service, version, targetTriple);
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath));
            File.WriteAllText(executablePath, string.Empty);
        }

        private static string GetLegacyBundledExecutablePath(AppConfigService service, string version, string targetTriple)
        {
            return Path.Combine(
                service.GetBundledCodexInstallDirectory(version, targetTriple),
                "vendor",
                targetTriple,
                "codex",
                "codex.exe");
        }

    }
}
