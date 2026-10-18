using System;
using System.IO;
using System.Text;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class UserCacheMaintenanceTests
    {
        [Test]
        public void CleanupOnStartup_RemovesOnlyRegenerableCachesAndLegacyByCtrlLog()
        {
            var rootPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "user-cache-" + Guid.NewGuid().ToString("N"));
            var service = new AppConfigService(rootPath);
            var updatePackage = Path.Combine(service.GetUpdateTempDirectory(), "1.2.3", "MyCodex.exe");
            var attachment = Path.Combine(service.GetAttachmentTempDirectory(), "paste.png");
            var unrelatedTempFile = Path.Combine(service.GetTempDirectory(), "preserve.dat");
            var appLog = Path.Combine(service.GetLogDirectory(), "app-current.log");
            var byCtrlLog = Path.Combine(service.GetLogDirectory(), "byctrl.log");

            try
            {
                WriteFile(updatePackage);
                WriteFile(attachment);
                WriteFile(unrelatedTempFile);
                WriteFile(appLog);
                WriteFile(byCtrlLog);

                UserCacheMaintenance.CleanupOnStartup(service);

                Assert.That(Directory.Exists(service.GetUpdateTempDirectory()), Is.False);
                Assert.That(Directory.Exists(service.GetAttachmentTempDirectory()), Is.False);
                Assert.That(File.Exists(byCtrlLog), Is.False);
                Assert.That(File.Exists(unrelatedTempFile), Is.True);
                Assert.That(File.Exists(appLog), Is.True);
            }
            finally
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, true);
                }
            }
        }

        [Test]
        public void CleanupOnStartup_SeparateTargetPreservesSiblingAndSharedServiceFiles()
        {
            var rootPath = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "target-cache-" + Guid.NewGuid().ToString("N"));
            var installationRoot = Path.Combine(rootPath, "installation");
            var sharedRoot = Path.Combine(rootPath, "channel");
            var first = new AppConfigService(Path.Combine(sharedRoot, "targets", "target"), installationRoot, "MyCodex", sharedRoot);
            var sibling = new AppConfigService(Path.Combine(sharedRoot, "targets", "target-sibling"), installationRoot, "MyCodex", sharedRoot);
            var firstAttachment = Path.Combine(first.GetAttachmentTempDirectory(), "paste.png");
            var firstUpdate = Path.Combine(first.GetUpdateTempDirectory(), "package.zip");
            var preservedFiles = new[]
            {
                first.GetConfigFilePath(),
                first.GetRemoteBindingsFilePath(),
                Path.Combine(sibling.GetAttachmentTempDirectory(), "paste.png"),
                Path.Combine(sibling.GetUpdateTempDirectory(), "package.zip"),
                Path.Combine(sibling.GetLogDirectory(), "byctrl.log"),
                Path.Combine(sharedRoot, "relay", "relay.db"),
                Path.Combine(installationRoot, "runtime", "relay", "relay.exe")
            };
            try
            {
                WriteFile(firstAttachment);
                WriteFile(firstUpdate);
                foreach (var path in preservedFiles)
                {
                    WriteFile(path);
                }

                UserCacheMaintenance.CleanupOnStartup(first);

                Assert.That(File.Exists(firstAttachment), Is.False);
                Assert.That(File.Exists(firstUpdate), Is.False);
                foreach (var path in preservedFiles)
                {
                    Assert.That(File.ReadAllText(path), Is.EqualTo("cache"), path);
                }
            }
            finally
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, true);
                }
            }
        }

        [Test]
        public void Program_RunsUserCacheMaintenanceBeforeConfiguringDiagnostics()
        {
            var source = File.ReadAllText(
                RepositorySource.FindSourceFile("Program.cs"),
                Encoding.UTF8);
            var cleanupIndex = source.IndexOf(
                "UserCacheMaintenance.CleanupOnStartup(appConfigService);",
                StringComparison.Ordinal);
            var diagnosticsIndex = source.IndexOf(
                "ConfigureDiagnostics(appConfigService);",
                StringComparison.Ordinal);

            Assert.That(cleanupIndex, Is.GreaterThan(0));
            Assert.That(diagnosticsIndex, Is.GreaterThan(cleanupIndex));
        }

        private static void WriteFile(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "cache");
        }
    }
}
