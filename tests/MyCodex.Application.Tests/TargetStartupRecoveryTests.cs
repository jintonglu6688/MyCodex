using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Xml;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetStartupRecoveryTests
    {
        private string _root;
        private AppConfigService _legacy;
        private AppConfigService _original;
        private TargetCatalogStore _catalog;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "target-startup-recovery", Guid.NewGuid().ToString("N"));
            _legacy = new AppConfigService(_root);
            new TargetDataMigration(_legacy).Run();
            _catalog = new TargetCatalogStore(_legacy);
            _original = _catalog.GetPaths("windows");
        }

        [Test]
        public void Startup_DamagedFileWithValidBackup_ReopensOriginalWindowsTarget(
            [Values("config.json", "app-state.json", "automations.json", "remote-service.json", "catalog.json", "relay-service.json")] string fileName,
            [Values("zero-filled", "truncated", "empty")] string damage)
        {
            var path = ResolvePath(fileName);
            var saved = File.ReadAllBytes(path);
            File.Copy(path, path + ".bak", true);
            var damaged = damage == "zero-filled" ? new byte[saved.Length] :
                damage == "truncated" ? saved.Take(saved.Length / 2).ToArray() : new byte[0];
            File.WriteAllBytes(path, damaged);

            var reopened = TargetMigrationStartup.Resolve(_legacy, "MyCodex");

            Assert.That(reopened.GetAppDataDirectory(), Is.EqualTo(_original.GetAppDataDirectory()));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved));
            Assert.That(File.ReadAllBytes(path + ".bak"), Is.EqualTo(saved));
        }

        [Test]
        public void Startup_UnrecoverableFile_PreservesBothFilesAndReportsPaths(
            [Values("config.json", "app-state.json", "automations.json", "remote-service.json", "catalog.json", "relay-service.json")] string fileName,
            [Values(false, true)] bool hasBackup)
        {
            var path = ResolvePath(fileName);
            var damaged = new byte[28];
            File.WriteAllBytes(path, damaged);
            if (hasBackup) File.WriteAllBytes(path + ".bak", damaged);

            var error = Assert.Throws<PersistenceRecoveryRequiredException>(() => TargetMigrationStartup.Resolve(_legacy, "MyCodex"));

            Assert.That(error.PrimaryPath, Is.EqualTo(path));
            Assert.That(error.BackupPath, Is.EqualTo(path + ".bak"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(damaged));
            Assert.That(File.Exists(path + ".bak"), Is.EqualTo(hasBackup));
            if (hasBackup) Assert.That(File.ReadAllBytes(path + ".bak"), Is.EqualTo(damaged));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path), "*.recovery-*.tmp"), Is.Empty);
        }

        [TestCase("config.json")]
        [TestCase("remote-service.json")]
        [TestCase("catalog.json")]
        public void Startup_BackupWithInvalidIdentity_DoesNotReplacePrimary(string fileName)
        {
            var path = ResolvePath(fileName);
            object invalid = fileName == "config.json"
                ? (object)new AppConfigSnapshot { CodexRuntimeSource = "Wsl", WslAppServer = new WslAppServerConfigSnapshot { DistributionName = "Debian" } }
                : fileName == "remote-service.json" ? (object)new TargetRemoteIdentitySnapshot
                {
                    HostId = "invalid", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                }
                : new TargetCatalogSnapshot();
            using (var stream = File.Create(path + ".bak"))
                new DataContractJsonSerializer(invalid.GetType()).WriteObject(stream, invalid);
            var damaged = new byte[28];
            File.WriteAllBytes(path, damaged);

            Assert.Throws<PersistenceRecoveryRequiredException>(() => TargetMigrationStartup.Resolve(_legacy, "MyCodex"));

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(damaged));
        }

        [TestCase("Wsl")]
        [TestCase("Ssh")]
        public void Startup_RecoversSelectedRemoteTargetWithoutReadingDamagedOtherTarget(string kind)
        {
            var target = kind == "Wsl"
                ? _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" })
                : _catalog.AddSsh("Build", new RemoteMacSshConfigSnapshot { HostName = "example", UserName = "alice", Port = 22 });
            _catalog.RememberOpened(target.Id);
            var paths = _catalog.GetPaths(target.Id);
            var path = paths.GetConfigFilePath();
            var saved = File.ReadAllBytes(path);
            File.Copy(path, path + ".bak", true);
            File.WriteAllBytes(path, new byte[28]);
            File.WriteAllBytes(_original.GetConfigFilePath(), new byte[28]);

            var reopened = TargetMigrationStartup.Resolve(_legacy, "MyCodex");

            Assert.That(reopened.GetAppDataDirectory(), Is.EqualTo(paths.GetAppDataDirectory()));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved));
            Assert.That(File.ReadAllBytes(_original.GetConfigFilePath()), Is.EqualTo(new byte[28]));
            Assert.That(_catalog.Load().LastOpenedTargetId, Is.EqualTo(target.Id));
        }

        [Test]
        [NonParallelizable]
        public void StartupFailure_WritesEarlyLogAndSafeRecoveryMessage()
        {
            Program.ConfigureDiagnostics(_legacy);
            try
            {
                var path = _original.GetAppStateFilePath();
                File.WriteAllBytes(path, new byte[28]);
                var error = Assert.Throws<PersistenceRecoveryRequiredException>(() => TargetMigrationStartup.Resolve(_legacy, "MyCodex"));
                Program.LogStartupFailure(error);
                Program.LogStartupFailure(new XmlException("secret-bearing-input"));

                string log;
                using (var stream = new FileStream(Directory.GetFiles(_legacy.GetLogDirectory(), "app-*.log").Single(),
                    FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                    log = reader.ReadToEnd();
                Assert.That(log, Does.Contain("[Startup] Target startup failed"));
                Assert.That(log, Does.Contain(path));
                Assert.That(log, Does.Contain("System.Xml.XmlException"));
                Assert.That(log, Does.Not.Contain("secret-bearing-input"));
                var message = Program.BuildStartupFailureMessage(error);
                Assert.That(message, Does.Contain(path));
                Assert.That(message, Does.Contain(path + ".bak"));
                Assert.That(Program.BuildStartupFailureMessage(new XmlException("secret-bearing-input")), Does.Not.Contain("secret-bearing-input"));
            }
            finally
            {
                var listener = Trace.Listeners["MyCodexFileTraceListener"];
                Trace.Listeners.Remove(listener);
                listener?.Dispose();
            }
        }

        private string ResolvePath(string fileName)
        {
            if (fileName == "catalog.json") return Path.Combine(_root, "targets", fileName);
            if (fileName == "relay-service.json") return Path.Combine(_legacy.GetConfigDirectory(), fileName);
            return Path.Combine(_original.GetConfigDirectory(), fileName);
        }
    }
}
