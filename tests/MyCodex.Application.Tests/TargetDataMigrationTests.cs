using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.WinForms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetDataMigrationTests
    {
        private string _root;
        private AppConfigService _legacy;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "target-migration", Guid.NewGuid().ToString("N"));
            _legacy = new AppConfigService(_root);
        }

        [TestCase("Bundled")]
        [TestCase("System")]
        [TestCase("Custom")]
        [TestCase("Wsl")]
        [TestCase("RemoteMacSsh")]
        [TestCase("RemoteUnixSsh")]
        public void Upgrade_RetainsOriginalTargetPreferencesIdentityAndSharedService(string source)
        {
            var config = CreateConfig(source);
            var remote = CreateRemote();
            Write(_legacy.GetConfigFilePath(), config);
            Write(_legacy.GetRemoteServiceConfigFilePath(), remote);
            var bindingStore = new JsonRemoteBindingStore(_legacy.GetRemoteBindingsFilePath());
            bindingStore.SavePending(new MyCodex.Application.RemoteRelay.RemoteDeviceBinding
            {
                TenantId = remote.TenantId, HostId = remote.HostId, DeviceId = "phone", ClaimId = "claim-1"
            });
            var bindingBytes = File.ReadAllBytes(_legacy.GetRemoteBindingsFilePath());
            var configBytes = File.ReadAllBytes(_legacy.GetConfigFilePath());
            Directory.CreateDirectory(Path.Combine(_root, "Relay"));
            File.WriteAllText(Path.Combine(_root, "Relay", "relay-state.db"), "existing-db");
            File.WriteAllText(Path.Combine(_root, "Relay", "cert.pem"), "existing-cert");

            var migration = new TargetDataMigration(_legacy);
            var catalog = migration.Run();
            var paths = migration.GetStartupPaths(catalog, "MyCodex");
            var preferences = new JsonAppConfigStore(paths).Load();
            var migratedRemote = new JsonRemoteServiceConfigStore(paths).Load();
            Assert.That(preferences.CodexRuntimeSource, Is.EqualTo(source == "RemoteMacSsh" ? "RemoteUnixSsh" : source));
            Assert.That(preferences.UiThemePreference, Is.EqualTo("dark"));
            Assert.That(preferences.EnableAutomaticChatReading, Is.True);
            Assert.That(preferences.MainWindowHotKeyVirtualKey, Is.EqualTo(0x4B));
            Assert.That(migratedRemote.HostId, Is.EqualTo(remote.HostId));
            Assert.That(migratedRemote.LastRegisteredAt, Is.EqualTo(remote.LastRegisteredAt));
            Assert.That(migratedRemote.EncryptedAccessSecret, Is.EqualTo(remote.EncryptedAccessSecret));
            Assert.That(migratedRemote.EmbeddedRelayCertificateSha256, Is.EqualTo(remote.EmbeddedRelayCertificateSha256));
            Assert.That(migratedRemote.RelaySigningKeyFingerprint, Is.EqualTo(remote.RelaySigningKeyFingerprint));
            Assert.That(migratedRemote.EmbeddedRelayStatePath, Is.EqualTo(remote.EmbeddedRelayStatePath));
            Assert.That(File.ReadAllBytes(paths.GetRemoteBindingsFilePath()), Is.EqualTo(bindingBytes));
            Assert.That(File.ReadAllBytes(_legacy.GetConfigFilePath()), Is.EqualTo(configBytes));
            Assert.That(File.ReadAllBytes(Path.Combine(_root, "migration-backup", "config", "config.json")), Is.EqualTo(configBytes));
            Assert.That(File.ReadAllText(Path.Combine(_root, "Relay", "relay-state.db")), Is.EqualTo("existing-db"));
            Assert.That(File.ReadAllText(Path.Combine(_root, "Relay", "cert.pem")), Is.EqualTo("existing-cert"));
            Assert.That(paths.GetRuntimeDirectory(), Is.EqualTo(_legacy.GetRuntimeDirectory()));
            foreach (var target in catalog.Targets.Where(t => t.Id != catalog.LastOpenedTargetId))
            {
                var otherPaths = Paths(target.Id);
                var other = new JsonAppConfigStore(otherPaths).Load();
                var identity = new JsonRemoteServiceConfigStore(otherPaths).Load();
                Assert.That(identity.HostId, Is.Not.EqualTo(remote.HostId));
                Assert.That(identity.RemoteEnabled, Is.False);
                Assert.That(other.MainWindowHotKeyEnabled, Is.False);
                Assert.That(other.EnableAutomaticChatReading, Is.False);
                Assert.That(File.Exists(otherPaths.GetRemoteBindingsFilePath()), Is.False);
            }
        }

        [Test]
        public void Upgrade_PartitionsKnownStateAndDisablesUnknownAutomations()
        {
            Write(_legacy.GetConfigFilePath(), CreateConfig("Wsl"));
            Write(_legacy.GetAppStateFilePath(), new AppStateSnapshot
            {
                Sessions = new List<ChatSession>
                {
                    new ChatSession { Id = "win", RuntimeProfileId = "windows", Title = "Local history" },
                    new ChatSession { Id = "old-win", Title = "Unscoped local history" },
                    new ChatSession { Id = "wsl", RuntimeProfileId = "wsl|ubuntu", Title = "WSL history" },
                    new ChatSession { Id = "office", Mode = AppInteractionMode.Office, OfficeRuntimeScopeId = "wsl|ubuntu", Title = "Office retained" },
                    new ChatSession { Id = "unknown", RuntimeProfileId = "remote-unix|missing", Title = "Unresolved history" }
                },
                OfficeRuntimeStates = new List<OfficeRuntimeStateSnapshot>
                {
                    new OfficeRuntimeStateSnapshot { RuntimeScopeId = "wsl|ubuntu", CurrentWorkspaceId = "office-workspace" }
                }
            });
            Write(_legacy.GetAutomationsFilePath(), new AutomationStateSnapshot
            {
                Definitions = new List<AutomationDefinitionSnapshot>
                {
                    Job("windows-job", "windows"), Job("wsl-job", "wsl|ubuntu"), Job("unknown-job", "remote-unix|missing"), Job("unscoped-job", null)
                }
            });
            Write(_legacy.GetModeSettingsFilePath(), new ModeSettingsSnapshot { OfficeRootPath = @"D:\Documents\Office" });
            var catalog = new TargetDataMigration(_legacy).Run();
            var active = Paths(catalog.LastOpenedTargetId);
            Assert.That(Read<AppStateSnapshot>(active.GetAppStateFilePath()).Sessions.Select(s => s.Id), Is.EquivalentTo(new[] { "wsl", "office" }));
            Assert.That(Read<AppStateSnapshot>(Paths("windows").GetAppStateFilePath()).Sessions.Select(s => s.Id), Is.EquivalentTo(new[] { "win", "old-win" }));
            Assert.That(Read<AppStateSnapshot>(Path.Combine(_root, "targets", "pending-state.json")).Sessions.Single().Title, Is.EqualTo("Unresolved history"));
            Assert.That(Read<AutomationStateSnapshot>(active.GetAutomationsFilePath()).Definitions.Single().Id, Is.EqualTo("wsl-job"));
            var pending = Read<AutomationStateSnapshot>(Path.Combine(_root, "targets", "pending-automations.json"));
            Assert.That(pending.Definitions.Select(d => d.Id), Is.EquivalentTo(new[] { "unknown-job", "unscoped-job" }));
            Assert.That(pending.Definitions.All(d => !d.Enabled && d.NextRunAt == null), Is.True);
            Assert.That(Read<ModeSettingsSnapshot>(Paths("windows").GetModeSettingsFilePath()).OfficeRootPath, Is.EqualTo(@"D:\Documents\Office"));
            Assert.That(Directory.Exists(Path.Combine(_root, "remote-unix")), Is.False);
        }

        [TestCase("backup")]
        [TestCase("target:windows")]
        [TestCase("validated")]
        [TestCase("shared")]
        public void Failure_DoesNotPublishPartialTargetsAndCanRetry(string checkpoint)
        {
            Write(_legacy.GetConfigFilePath(), CreateConfig("Wsl"));
            Write(_legacy.GetRemoteServiceConfigFilePath(), CreateRemote());
            var original = File.ReadAllBytes(_legacy.GetRemoteServiceConfigFilePath());
            var failing = new TargetDataMigration(_legacy, phase => { if (phase == checkpoint) throw new IOException("injected"); });
            Assert.Throws<IOException>(() => failing.Run());
            Assert.That(File.Exists(Path.Combine(_root, "targets", "catalog.json")), Is.False);
            Assert.That(File.ReadAllBytes(_legacy.GetRemoteServiceConfigFilePath()), Is.EqualTo(original));
            var migration = new TargetDataMigration(_legacy);
            var catalog = migration.Run();
            var paths = migration.GetStartupPaths(catalog, "MyCodex");
            Assert.That(new JsonRemoteServiceConfigStore(paths).Load().HostId, Is.EqualTo(CreateRemote().HostId));
            var updated = new JsonAppConfigStore(paths).Load();
            updated.UiThemePreference = "light";
            new JsonAppConfigStore(paths).Save(updated);
            Assert.That(migration.Run().LastOpenedTargetId, Is.EqualTo(catalog.LastOpenedTargetId));
            Assert.That(new JsonAppConfigStore(paths).Load().UiThemePreference, Is.EqualTo("light"));
        }

        [Test]
        public void CorruptPrimary_UsesPreservedBackupWithoutReplacingOriginal()
        {
            Write(_legacy.GetConfigFilePath() + ".bak", CreateConfig("Wsl"));
            File.WriteAllText(_legacy.GetConfigFilePath(), "broken");
            var catalog = new TargetDataMigration(_legacy).Run();
            Assert.That(catalog.LastOpenedTargetId, Does.StartWith("wsl-"));
            Assert.That(File.ReadAllText(_legacy.GetConfigFilePath()), Is.EqualTo("broken"));
        }

        [Test]
        public void CorruptDataWithoutBackup_DoesNotSilentlyStartWindows()
        {
            Directory.CreateDirectory(_legacy.GetConfigDirectory());
            File.WriteAllText(_legacy.GetConfigFilePath(), "broken");
            Assert.Throws<System.Runtime.Serialization.SerializationException>(() => new TargetDataMigration(_legacy).Run());
            Assert.That(File.Exists(Path.Combine(_root, "targets", "catalog.json")), Is.False);
        }

        [Test]
        public void ConcurrentMigration_HasOneWriterAndRetryReadsCompletedLayout()
        {
            Write(_legacy.GetConfigFilePath(), CreateConfig("Wsl"));
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var first = Task.Run(() => new TargetDataMigration(_legacy, phase =>
                {
                    if (phase == "backup") { entered.Set(); Assert.That(release.Wait(TimeSpan.FromSeconds(10)), Is.True); }
                }).Run());
                Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
                try { Assert.Throws<IOException>(() => new TargetDataMigration(_legacy).Run()); }
                finally { release.Set(); }
                Assert.That(first.GetAwaiter().GetResult().Targets.Count(t => t.Kind == "Windows"), Is.EqualTo(1));
                Assert.That(new TargetDataMigration(_legacy).Run().LastOpenedTargetId, Does.StartWith("wsl-"));
            }
        }

        [Test]
        public void CatalogPathTraversal_IsRejectedBeforeReadingOtherDirectories()
        {
            Write(_legacy.GetConfigFilePath(), CreateConfig("Bundled"));
            var migration = new TargetDataMigration(_legacy);
            var catalog = migration.Run();
            catalog.Targets.Add(new TargetProfileSnapshot { Id = "../outside", Kind = "Wsl" });
            Write(Path.Combine(_root, "targets", "catalog.json"), catalog);
            Assert.Throws<InvalidDataException>(() => migration.Run());
        }

        [Test]
        public void Catalog_UsesConcreteStableIdsAndDoesNotInventDefaultWslTarget()
        {
            var planner = new TargetMigrationPlanner();
            Assert.That(planner.CreateCatalog(new AppConfigSnapshot(), new AppStateSnapshot()).Targets.Select(t => t.Kind), Is.EqualTo(new[] { "Windows" }));
            var config = CreateConfig("RemoteUnixSsh");
            var first = planner.CreateCatalog(config, new AppStateSnapshot());
            config.RemoteUnixSshProfiles[0].Name = "Renamed server";
            var second = planner.CreateCatalog(config, new AppStateSnapshot());
            Assert.That(second.LastOpenedTargetId, Is.EqualTo(first.LastOpenedTargetId));
            Assert.That(second.Targets.Single(t => t.Kind == "Ssh").Name, Is.EqualTo("Renamed server"));
        }

        [Test]
        public void DocumentsLayout_IsBackedUpAndMigratedWithoutImportingDefaults()
        {
            var documents = Path.Combine(_root, "old-documents");
            Write(Path.Combine(documents, "config.json"), CreateConfig("Wsl"));
            Write(Path.Combine(documents, "app-state.json"), new AppStateSnapshot
            {
                Sessions = new List<ChatSession> { new ChatSession { Id = "original", RuntimeProfileId = "windows", Title = "Old history" } }
            });
            var original = File.ReadAllBytes(Path.Combine(documents, "config.json"));
            var catalog = new TargetDataMigration(_legacy, null, documents).Run();
            Assert.That(catalog.LastOpenedTargetId, Does.StartWith("wsl-"));
            Assert.That(new JsonAppConfigStore(Paths(catalog.LastOpenedTargetId)).Load().EnableAutomaticChatReading, Is.True);
            Assert.That(Read<AppStateSnapshot>(Paths("windows").GetAppStateFilePath()).Sessions.Single().Title, Is.EqualTo("Old history"));
            Assert.That(File.ReadAllBytes(Path.Combine(_root, "migration-backup", "legacy-config", "config.json")), Is.EqualTo(original));
            Assert.That(File.ReadAllBytes(Path.Combine(documents, "config.json")), Is.EqualTo(original));
        }

        [Test]
        public void CompletedCatalogWithoutSharedConfiguration_IsNotOpenedWithDefaultCredentials()
        {
            Write(_legacy.GetConfigFilePath(), CreateConfig("Wsl"));
            var migration = new TargetDataMigration(_legacy);
            migration.Run();
            File.Delete(Path.Combine(_legacy.GetConfigDirectory(), "relay-service.json"));
            Assert.Throws<FileNotFoundException>(() => migration.Run());
        }

        [TestCase("RemoteMacSsh")]
        [TestCase("Bundled")]
        public void LegacySshFallback_IsOneSavedTargetAndOldScopesFollowIt(string source)
        {
            Directory.CreateDirectory(_legacy.GetConfigDirectory());
            File.WriteAllText(_legacy.GetConfigFilePath(), "{\"CodexRuntimeSource\":\"" + source + "\",\"RemoteMacSsh\":{\"HostName\":\"server\",\"UserName\":\"dev\",\"Port\":22}}");
            File.WriteAllText(_legacy.GetAppStateFilePath(), "{\"Sessions\":[{\"Id\":\"office\",\"Mode\":1,\"RuntimeProfileId\":\"remote-unix|dev|server|22\",\"WorkspaceId\":\"remote-office\",\"Title\":\"Original Office\"}],\"RecentWorkspaces\":[{\"Id\":\"remote-office\",\"RootPath\":\"/home/dev/office\"}]}");
            File.WriteAllText(_legacy.GetAutomationsFilePath(), "{\"Definitions\":[{\"Id\":\"remote-job\",\"Enabled\":true,\"Context\":{\"RuntimeProfileId\":\"remote-unix|dev|server|22\"}}],\"Runs\":[{\"Id\":\"orphan\",\"AutomationId\":\"deleted-definition\"}]}");
            var catalog = new TargetDataMigration(_legacy).Run();
            var target = catalog.Targets.Single(t => t.Kind == "Ssh");
            var config = new JsonAppConfigStore(Paths(target.Id)).Load();
            Assert.That(new RuntimeProfileService().GetCurrentRuntimeProfileId(config), Is.EqualTo(target.RuntimeScopeId));
            var history = Read<AppStateSnapshot>(Paths(target.Id).GetAppStateFilePath());
            Assert.That(history.Sessions.Single().OfficeRuntimeScopeId, Is.EqualTo(target.RuntimeScopeId));
            Assert.That(history.RecentWorkspaces.Single().RootPath, Is.EqualTo("/home/dev/office"));
            Assert.That(Read<AutomationStateSnapshot>(Paths(target.Id).GetAutomationsFilePath()).Definitions.Single().Context.RuntimeProfileId, Is.EqualTo(target.RuntimeScopeId));
            Assert.That(Read<AutomationStateSnapshot>(Path.Combine(_root, "targets", "pending-automations.json")).Runs.Single().Id, Is.EqualTo("orphan"));
            Assert.That(Read<AppStateSnapshot>(Path.Combine(_root, "targets", "pending-state.json")).RecentWorkspaces, Is.Empty);
            Assert.That(catalog.LastOpenedTargetId, Is.EqualTo(source == "Bundled" ? "windows" : target.Id));
        }

        [Test]
        public void MissingDatesAndUnscopedOffice_AreRetainedWithoutGuessingWindows()
        {
            Directory.CreateDirectory(_legacy.GetConfigDirectory());
            File.WriteAllText(_legacy.GetConfigFilePath(), "{\"CodexRuntimeSource\":\"Bundled\"}");
            File.WriteAllText(_legacy.GetAppStateFilePath(), "{\"Sessions\":[{\"Id\":\"local\",\"Title\":\"Old local\"},{\"Id\":\"office\",\"Mode\":1,\"WorkspaceId\":\"office-old\",\"Title\":\"Unknown Office\"}],\"RecentWorkspaces\":[{\"Id\":\"office-old\",\"RootPath\":\"/home/user/office\"}]}");
            var catalog = new TargetDataMigration(_legacy).Run();
            Assert.That(Read<AppStateSnapshot>(Paths("windows").GetAppStateFilePath()).Sessions.Single().Id, Is.EqualTo("local"));
            var pending = Read<AppStateSnapshot>(Path.Combine(_root, "targets", "pending-state.json"));
            Assert.That(pending.Sessions.Single().Title, Is.EqualTo("Unknown Office"));
            Assert.That(pending.RecentWorkspaces.Single().RootPath, Is.EqualTo("/home/user/office"));
            Assert.That(catalog.LastOpenedTargetId, Is.EqualTo("windows"));
        }

        [Test]
        public void RetryAfterOldVersionChangesSharedCredentials_PublishesCurrentSource()
        {
            Write(_legacy.GetConfigFilePath(), CreateConfig("Wsl"));
            var remote = CreateRemote();
            Write(_legacy.GetRemoteServiceConfigFilePath(), remote);
            Assert.Throws<IOException>(() => new TargetDataMigration(_legacy, phase => { if (phase == "shared") throw new IOException("injected"); }).Run());
            remote.TenantId = "updated-tenant";
            remote.EncryptedAccessSecret = "protected-updated";
            Write(_legacy.GetRemoteServiceConfigFilePath(), remote);
            var migration = new TargetDataMigration(_legacy);
            var catalog = migration.Run();
            var result = new JsonRemoteServiceConfigStore(migration.GetStartupPaths(catalog, "MyCodex")).Load();
            Assert.That(result.TenantId, Is.EqualTo("updated-tenant"));
            Assert.That(result.EncryptedAccessSecret, Is.EqualTo("protected-updated"));
            Assert.That(result.HostId, Is.EqualTo(remote.HostId));
        }

        [Test]
        public void SingleTargetStartup_ResolvesFreshWindowsAndReopensSavedData()
        {
            var paths = TargetMigrationStartup.Resolve(_legacy, "MyCodex");
            Assert.That(paths.GetAppDataDirectory(), Is.EqualTo(Path.Combine(_root, "targets", "windows")));
            Assert.That(paths.GetSharedDataDirectory(), Is.EqualTo(_root));
            Assert.That(new JsonRemoteServiceConfigStore(paths).Load().RemoteEnabled, Is.False);
            var config = new JsonAppConfigStore(paths).Load();
            config.UiThemePreference = "dark";
            new JsonAppConfigStore(paths).Save(config);
            var reopened = TargetMigrationStartup.Resolve(_legacy, "MyCodex");
            Assert.That(new JsonAppConfigStore(reopened).Load().UiThemePreference, Is.EqualTo("dark"));
        }

        [Test]
        public void CompletedCatalogStartup_DoesNotRevalidateOrRepairAnotherTarget()
        {
            Write(_legacy.GetConfigFilePath(), CreateConfig("Wsl"));
            var catalog = new TargetDataMigration(_legacy).Run();
            Assert.That(catalog.LastOpenedTargetId, Is.Not.EqualTo("windows"));
            var inactiveConfig = Paths("windows").GetConfigFilePath();
            File.WriteAllText(inactiveConfig, "invalid inactive target configuration");

            var selectedPaths = TargetMigrationStartup.Resolve(_legacy, "MyCodex");

            Assert.That(selectedPaths.GetAppDataDirectory(), Is.EqualTo(Paths(catalog.LastOpenedTargetId).GetAppDataDirectory()));
            Assert.That(File.ReadAllText(inactiveConfig), Is.EqualTo("invalid inactive target configuration"));
        }

        private AppConfigService Paths(string id)
        {
            return new AppConfigService(Path.Combine(_root, "targets", id), _root, "MyCodex", _root);
        }

        private static AppConfigSnapshot CreateConfig(string source)
        {
            return new AppConfigSnapshot
            {
                CodexRuntimeSource = source, CodexCustomExecutablePath = @"D:\codex\codex.exe", UiThemePreference = "dark",
                EnableAutomaticChatReading = true, MainWindowHotKeyVirtualKey = 0x4B,
                RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                {
                    new RemoteUnixSshProfileConfigSnapshot { Id = "ssh-original", Name = "Saved server", Ssh = new RemoteMacSshConfigSnapshot { HostName = "server.example", UserName = "dev" } }
                },
                SelectedRemoteUnixSshProfileId = "ssh-original"
            };
        }

        private RemoteServiceConfigSnapshot CreateRemote()
        {
            return new RemoteServiceConfigSnapshot
            {
                HostId = "ac238b63-ef0f-4b84-9bdb-c9a45400b7f3", ThisPcName = "Original device", RemoteServiceMode = "Embedded",
                RelayHost = "192.0.2.10", RelayPort = 38443, TenantId = "original-tenant", EncryptedAccessSecret = "protected-original",
                EmbeddedRelayConfigPath = Path.Combine(_root, "Relay", "relay-config.json"), EmbeddedRelayStatePath = Path.Combine(_root, "Relay", "relay-state.db"),
                EmbeddedRelayCertificateSha256 = new string('a', 64), RelaySigningKeyFingerprint = "original-pin", RelaySigningPublicKey = "original-public-key",
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastRegisteredAt = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            };
        }

        private static AutomationDefinitionSnapshot Job(string id, string scope)
        {
            return new AutomationDefinitionSnapshot { Id = id, Enabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                NextRunAt = DateTime.UtcNow, Context = new AutomationContextSnapshot { RuntimeProfileId = scope } };
        }

        private static void Write<T>(string path, T value)
        {
            if (value is AppStateSnapshot state)
            {
                foreach (var session in state.Sessions)
                {
                    session.CreatedAt = DateTime.UtcNow;
                    session.LastActiveAt = DateTime.UtcNow;
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = File.Create(path)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        }

        private static T Read<T>(string path)
        {
            using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }
}
