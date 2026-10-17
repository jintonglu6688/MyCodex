using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class JsonAutomationStoreTests
    {
        [Test]
        public void Load_ReturnsEmptySnapshotWhenMissing()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                var store = new JsonAutomationStore(service);

                var snapshot = store.Load();

                Assert.That(snapshot, Is.Not.Null);
                Assert.That(snapshot.Definitions, Is.Empty);
                Assert.That(snapshot.Runs, Is.Empty);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void SaveAndLoad_RoundTripsAndPrunesRunsPerAutomation()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                var store = new JsonAutomationStore(service);
                var runs = new List<AutomationRunSnapshot>();
                for (var i = 0; i < 105; i++)
                {
                    runs.Add(new AutomationRunSnapshot
                    {
                        Id = "run-" + i.ToString("000"),
                        AutomationId = "automation-1",
                        Status = AutomationRunStatus.Completed,
                        StartedAt = new DateTime(2026, 6, 5, 8, 0, 0).AddMinutes(i)
                    });
                }

                store.Save(new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-1",
                            Name = "Daily news",
                            Kind = AutomationKind.Standalone,
                            Enabled = true,
                            Prompt = "Summarize news."
                        }
                    },
                    Runs = runs
                });

                var reloaded = store.Load();

                Assert.That(File.Exists(service.GetAutomationsFilePath() + ".tmp"), Is.False);
                Assert.That(File.Exists(service.GetAutomationsFilePath() + ".bak"), Is.False);
                Assert.That(reloaded.Definitions, Has.Count.EqualTo(1));
                Assert.That(reloaded.Runs, Has.Count.EqualTo(100));
                Assert.That(reloaded.Runs[0].Id, Is.EqualTo("run-005"));
                Assert.That(reloaded.Runs[99].Id, Is.EqualTo("run-104"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void SaveAndLoad_NormalizesMissingInputItemsToEmptyList()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                var store = new JsonAutomationStore(service);
                var definition = new AutomationDefinitionSnapshot
                {
                    Id = "automation-1",
                    Name = "Daily news",
                    Kind = AutomationKind.Standalone,
                    Enabled = true,
                    Prompt = "Summarize news.",
                    InputItems = null
                };

                store.Save(new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot> { definition },
                    Runs = new List<AutomationRunSnapshot>()
                });

                var reloaded = store.Load();

                Assert.That(reloaded.Definitions, Has.Count.EqualTo(1));
                Assert.That(reloaded.Definitions[0].InputItems, Is.Not.Null);
                Assert.That(reloaded.Definitions[0].InputItems, Is.Empty);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Mutate_TwoStoresForSameFileSerializeLoadMutationSave()
        {
            var root = CreateTempDirectory();
            var releaseFirst = new ManualResetEventSlim();
            try
            {
                var service = new AppConfigService(root);
                var firstStore = new JsonAutomationStore(service);
                var secondStore = new JsonAutomationStore(service);
                var firstEntered = new ManualResetEventSlim();
                var secondStarted = new ManualResetEventSlim();
                var secondEntered = new ManualResetEventSlim();
                var firstTask = Task.Run(() => firstStore.Mutate(snapshot =>
                {
                    firstEntered.Set();
                    releaseFirst.Wait();
                    snapshot.Definitions.Add(new AutomationDefinitionSnapshot { Id = "first" });
                    return snapshot;
                }));
                Assert.That(firstEntered.Wait(5000), Is.True);

                var secondTask = Task.Run(() =>
                {
                    secondStarted.Set();
                    return secondStore.Mutate(snapshot =>
                    {
                        secondEntered.Set();
                        snapshot.Definitions.Add(new AutomationDefinitionSnapshot { Id = "second" });
                        return snapshot;
                    });
                });
                Assert.That(secondStarted.Wait(5000), Is.True);
                Assert.That(secondEntered.Wait(200), Is.False, "The second mutation entered before the first file transaction completed.");

                releaseFirst.Set();
                Assert.That(Task.WaitAll(new Task[] { firstTask, secondTask }, 5000), Is.True);
                Assert.That(firstStore.Load().Definitions.Select(item => item.Id), Is.EquivalentTo(new[] { "first", "second" }));
            }
            finally
            {
                releaseFirst.Set();
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Mutate_DifferentFilesDoNotShareOneGlobalLock()
        {
            var firstRoot = CreateTempDirectory();
            var secondRoot = CreateTempDirectory();
            var releaseFirst = new ManualResetEventSlim();
            try
            {
                var firstStore = new JsonAutomationStore(new AppConfigService(firstRoot));
                var secondStore = new JsonAutomationStore(new AppConfigService(secondRoot));
                var firstEntered = new ManualResetEventSlim();
                var secondEntered = new ManualResetEventSlim();
                var firstTask = Task.Run(() => firstStore.Mutate(snapshot =>
                {
                    firstEntered.Set();
                    releaseFirst.Wait();
                    return snapshot;
                }));
                Assert.That(firstEntered.Wait(5000), Is.True);

                var secondTask = Task.Run(() => secondStore.Mutate(snapshot =>
                {
                    secondEntered.Set();
                    return snapshot;
                }));

                Assert.That(secondEntered.Wait(5000), Is.True, "A mutation for another automation file was blocked.");
                Assert.That(secondTask.Wait(5000), Is.True);
                releaseFirst.Set();
                Assert.That(firstTask.Wait(5000), Is.True);
            }
            finally
            {
                releaseFirst.Set();
                DeleteDirectory(firstRoot);
                DeleteDirectory(secondRoot);
            }
        }

        [Test]
        public void Mutate_NullMutationThrowsWithoutChangingState()
        {
            var root = CreateTempDirectory();
            try
            {
                var store = new JsonAutomationStore(new AppConfigService(root));
                store.Save(new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot { Id = "existing" }
                    }
                });

                Assert.Throws<ArgumentNullException>(() => store.Mutate(null));
                Assert.That(store.Load().Definitions.Single().Id, Is.EqualTo("existing"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Mutate_MutationReturningNullThrowsWithoutChangingState()
        {
            var root = CreateTempDirectory();
            try
            {
                var store = new JsonAutomationStore(new AppConfigService(root));
                store.Save(new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot { Id = "existing" }
                    }
                });

                var exception = Assert.Throws<InvalidOperationException>(() => store.Mutate(snapshot => null));
                Assert.That(exception.Message, Is.EqualTo("Automation mutation returned null."));
                Assert.That(store.Load().Definitions.Single().Id, Is.EqualTo("existing"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Load_CorruptPrimaryAndValidBackup_RestoresBackup()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                var store = new JsonAutomationStore(service);
                store.Save(new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "known-automation",
                            Name = "Known",
                            Kind = AutomationKind.Standalone
                        }
                    }
                });
                File.Copy(service.GetAutomationsFilePath(), service.GetAutomationsFilePath() + ".bak", true);
                File.WriteAllText(service.GetAutomationsFilePath(), "{ invalid json");

                var snapshot = store.Load();

                Assert.That(snapshot.Definitions[0].Id, Is.EqualTo("known-automation"));
                Assert.That(new JsonAutomationStore(service).Load().Definitions[0].Id, Is.EqualTo("known-automation"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Load_ZeroFilledPrimaryAndValidBackup_RestoresBackup()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                var store = new JsonAutomationStore(service);
                store.Save(new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "known-automation",
                            Name = "Known",
                            Kind = AutomationKind.Standalone
                        }
                    }
                });
                var primaryPath = service.GetAutomationsFilePath();
                var backupPath = primaryPath + ".bak";
                File.Copy(primaryPath, backupPath, true);
                File.WriteAllBytes(primaryPath, new byte[new FileInfo(primaryPath).Length]);

                var snapshot = store.Load();

                Assert.That(snapshot.Definitions[0].Id, Is.EqualTo("known-automation"));
                Assert.That(File.ReadAllBytes(primaryPath), Is.EqualTo(File.ReadAllBytes(backupPath)));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Load_ZeroFilledPrimaryAndBackup_BlocksSave()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                service.EnsureConfigDirectory();
                var primaryPath = service.GetAutomationsFilePath();
                File.WriteAllBytes(primaryPath, new byte[28]);
                File.WriteAllBytes(primaryPath + ".bak", new byte[28]);
                var store = new JsonAutomationStore(service);

                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Load());
                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Save(new AutomationStateSnapshot()));
                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Mutate(snapshot => snapshot));
                Assert.That(File.ReadAllBytes(primaryPath), Is.EqualTo(new byte[28]));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Load_CorruptPrimaryAndBackup_BlocksSave()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                service.EnsureConfigDirectory();
                File.WriteAllText(service.GetAutomationsFilePath(), "{primary");
                File.WriteAllText(service.GetAutomationsFilePath() + ".bak", "{backup");
                var store = new JsonAutomationStore(service);

                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Load());
                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Save(new AutomationStateSnapshot()));
                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Mutate(snapshot => snapshot));
                Assert.That(File.ReadAllText(service.GetAutomationsFilePath()), Is.EqualTo("{primary"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Load_LockedFilePropagatesAndBlocksSave()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                service.EnsureConfigDirectory();
                var store = new JsonAutomationStore(service);
                store.Save(new AutomationStateSnapshot());
                using (new FileStream(service.GetAutomationsFilePath(), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Assert.Throws<IOException>(() => store.Load());
                }

                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Load());
                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Save(new AutomationStateSnapshot()));
                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Mutate(snapshot => snapshot));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Load_FilePathOccupiedByDirectory_DoesNotReturnEmpty()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                service.EnsureConfigDirectory();
                Directory.CreateDirectory(service.GetAutomationsFilePath());
                var store = new JsonAutomationStore(service);

                Assert.Throws<UnauthorizedAccessException>(() => store.Load());
                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Save(new AutomationStateSnapshot()));
                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Mutate(snapshot => snapshot));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void AppConfigService_ReturnsAutomationPathsUnderConfigAndData()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);

                Assert.That(service.GetAutomationsFilePath(), Is.EqualTo(Path.Combine(root, "config", "automations.json")));
                Assert.That(service.GetAutomationWorkspaceDirectory(), Is.EqualTo(Path.Combine(root, "automations", "workspace")));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "MyCodex-AutomationStoreTests-" + Guid.NewGuid().ToString("N"));
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
