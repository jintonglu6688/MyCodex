using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationServiceTests
    {
        [Test]
        public void Create_AddsDefinitionWithScheduleAndTimestamps()
        {
            var store = new InMemoryAutomationStore();
            var now = new DateTime(2026, 6, 5, 8, 0, 0);
            var service = new AutomationService(store, new AutomationScheduleCalculator(), () => now, () => "id-1");

            var definition = service.Create(new AutomationCreationRequest
            {
                Name = "Daily news",
                Kind = AutomationKind.Standalone,
                Prompt = "Each run, summarize important AI news.",
                Schedule = new AutomationScheduleSnapshot
                {
                    Kind = AutomationScheduleKind.EveryHours,
                    Interval = 2
                },
                Context = new AutomationContextSnapshot
                {
                    InteractionMode = AppInteractionMode.Coding,
                    WorkspacePath = @"E:\MyCodex"
                },
                ModelId = "gpt-5.5",
                ReasoningEffort = "high",
                ApprovalMode = ApprovalMode.ReadOnly,
                AllowNoFindingsAutoArchive = true
            });

            Assert.That(definition.Id, Is.EqualTo("id-1"));
            Assert.That(definition.Enabled, Is.True);
            Assert.That(definition.CreatedAt, Is.EqualTo(now));
            Assert.That(definition.UpdatedAt, Is.EqualTo(now));
            Assert.That(definition.NextRunAt, Is.EqualTo(now.AddHours(2)));
            Assert.That(store.Snapshot.Definitions, Has.Count.EqualTo(1));
        }

        [Test]
        public void Create_CopiesInputItemsIntoDefinition()
        {
            var store = new InMemoryAutomationStore();
            var service = new AutomationService(
                store,
                new AutomationScheduleCalculator(),
                () => new DateTime(2026, 6, 6, 8, 0, 0),
                () => "automation-1");
            var inputItems = new List<AutomationInputItemSnapshot>
            {
                new AutomationInputItemSnapshot
                {
                    Type = AutomationInputItemKind.Skill,
                    Name = "news-digest",
                    DisplayName = "News Digest",
                    Path = @"E:\MyCodex\.codex\skills\news-digest"
                }
            };

            var definition = service.Create(new AutomationCreationRequest
            {
                Name = "Daily news",
                Kind = AutomationKind.Standalone,
                Prompt = "Summarize news.",
                InputItems = inputItems,
                Schedule = new AutomationScheduleSnapshot { Kind = AutomationScheduleKind.EveryHours, Interval = 1 },
                Context = new AutomationContextSnapshot { WorkspacePath = @"E:\MyCodex" }
            });

            inputItems[0].Name = "mutated";
            Assert.That(definition.InputItems, Has.Count.EqualTo(1));
            Assert.That(definition.InputItems[0].Name, Is.EqualTo("news-digest"));
            Assert.That(store.Snapshot.Definitions[0].InputItems[0].Name, Is.EqualTo("news-digest"));
        }

        [Test]
        public void Update_ReplacesInputItemsWithCopiedValues()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(new AutomationDefinitionSnapshot
            {
                Id = "automation-1",
                Name = "Old",
                Kind = AutomationKind.Standalone,
                Prompt = "Old prompt",
                Enabled = true,
                Context = new AutomationContextSnapshot { WorkspacePath = @"E:\MyCodex" },
                InputItems = new List<AutomationInputItemSnapshot>
                {
                    new AutomationInputItemSnapshot
                    {
                        Type = AutomationInputItemKind.Skill,
                        Name = "old-skill",
                        DisplayName = "Old Skill",
                        Path = @"E:\old"
                    }
                }
            });
            var service = new AutomationService(
                store,
                new AutomationScheduleCalculator(),
                () => new DateTime(2026, 6, 6, 8, 0, 0),
                () => "unused");
            var inputItems = new List<AutomationInputItemSnapshot>
            {
                new AutomationInputItemSnapshot
                {
                    Type = AutomationInputItemKind.Plugin,
                    Name = "mycodex-office",
                    DisplayName = "MyCodex Office",
                    Path = @"E:\plugins\mycodex-office"
                }
            };

            var definition = service.Update(new AutomationUpdateRequest
            {
                Id = "automation-1",
                Name = "Updated",
                Kind = AutomationKind.Standalone,
                Prompt = "Updated prompt",
                InputItems = inputItems,
                Schedule = new AutomationScheduleSnapshot { Kind = AutomationScheduleKind.ManualOnly },
                Context = new AutomationContextSnapshot { WorkspacePath = @"E:\MyCodex" }
            });

            inputItems[0].Name = "mutated";
            Assert.That(definition.InputItems, Has.Count.EqualTo(1));
            Assert.That(definition.InputItems[0].Type, Is.EqualTo(AutomationInputItemKind.Plugin));
            Assert.That(definition.InputItems[0].Name, Is.EqualTo("mycodex-office"));
            Assert.That(store.Snapshot.Definitions[0].InputItems[0].Name, Is.EqualTo("mycodex-office"));
        }

        [Test]
        public void Create_RejectsEmptyPrompt()
        {
            var service = new AutomationService(new InMemoryAutomationStore(), new AutomationScheduleCalculator());

            Assert.Throws<ArgumentException>(() => service.Create(new AutomationCreationRequest
            {
                Name = "Daily news",
                Kind = AutomationKind.Standalone,
                Prompt = " ",
                Schedule = new AutomationScheduleSnapshot(),
                Context = new AutomationContextSnapshot { WorkspacePath = @"E:\MyCodex" }
            }));
        }

        [Test]
        public void DisableAndEnable_UpdateEnabledAndNextRun()
        {
            var store = new InMemoryAutomationStore();
            var now = new DateTime(2026, 6, 5, 8, 0, 0);
            var service = new AutomationService(store, new AutomationScheduleCalculator(), () => now, () => "id-1");
            var definition = service.Create(new AutomationCreationRequest
            {
                Name = "Daily news",
                Kind = AutomationKind.Standalone,
                Prompt = "Each run, summarize news.",
                Schedule = new AutomationScheduleSnapshot
                {
                    Kind = AutomationScheduleKind.EveryMinutes,
                    Interval = 10
                },
                Context = new AutomationContextSnapshot { WorkspacePath = @"E:\MyCodex" }
            });

            service.Disable(definition.Id);
            Assert.That(store.Snapshot.Definitions[0].Enabled, Is.False);
            Assert.That(store.Snapshot.Definitions[0].NextRunAt, Is.Null);

            service.Enable(definition.Id);
            Assert.That(store.Snapshot.Definitions[0].Enabled, Is.True);
            Assert.That(store.Snapshot.Definitions[0].NextRunAt, Is.EqualTo(now.AddMinutes(10)));
        }

        [Test]
        public void Delete_RemovesDefinitionAndRuns()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(new AutomationDefinitionSnapshot { Id = "automation-1" });
            store.Snapshot.Runs.Add(new AutomationRunSnapshot { Id = "run-1", AutomationId = "automation-1" });
            var service = new AutomationService(store, new AutomationScheduleCalculator());

            service.Delete("automation-1");

            Assert.That(store.Snapshot.Definitions, Is.Empty);
            Assert.That(store.Snapshot.Runs, Is.Empty);
        }

        [Test]
        public void DeleteRun_RemovesOnlySelectedRunFromLocalRecords()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(new AutomationDefinitionSnapshot { Id = "automation-1" });
            store.Snapshot.Runs.Add(new AutomationRunSnapshot { Id = "run-1", AutomationId = "automation-1" });
            store.Snapshot.Runs.Add(new AutomationRunSnapshot { Id = "run-2", AutomationId = "automation-1" });
            var service = new AutomationService(store, new AutomationScheduleCalculator());

            service.DeleteRun("run-1");

            Assert.That(store.Snapshot.Definitions, Has.Count.EqualTo(1));
            Assert.That(store.Snapshot.Runs, Has.Count.EqualTo(1));
            Assert.That(store.Snapshot.Runs[0].Id, Is.EqualTo("run-2"));
        }

        [Test]
        public void MarkRunRead_ClearsUnreadOnly()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Runs.Add(new AutomationRunSnapshot
            {
                Id = "run-1",
                Unread = true,
                Archived = true,
                NeedsAttention = true
            });
            var service = new AutomationService(store, new AutomationScheduleCalculator());

            service.MarkRunRead("run-1");

            Assert.That(store.Snapshot.Runs[0].Unread, Is.False);
            Assert.That(store.Snapshot.Runs[0].Archived, Is.True);
            Assert.That(store.Snapshot.Runs[0].NeedsAttention, Is.True);
        }

        [Test]
        public void ArchiveRun_ClearsPendingStateAndKeepsRunInHistory()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Runs.Add(new AutomationRunSnapshot
            {
                Id = "run-1",
                Unread = true,
                Archived = false,
                NeedsAttention = true
            });
            var service = new AutomationService(store, new AutomationScheduleCalculator());

            service.ArchiveRun("run-1");

            Assert.That(store.Snapshot.Runs[0].Archived, Is.True);
            Assert.That(store.Snapshot.Runs[0].Unread, Is.False);
            Assert.That(store.Snapshot.Runs[0].NeedsAttention, Is.False);
            Assert.That(service.ListRuns(), Has.Count.EqualTo(1));
            Assert.That(service.ListRuns()[0].Id, Is.EqualTo("run-1"));
        }

        [Test]
        public void RunTriageActions_UnknownRunIdThrowsLocalizedError()
        {
            var service = new AutomationService(new InMemoryAutomationStore(), new AutomationScheduleCalculator());
            var expectedMessage = SharedLocalization.Text("AutomationService.Error.RunNotFound", "Automation run was not found.");

            var markReadException = Assert.Throws<InvalidOperationException>(() => service.MarkRunRead("missing-run"));
            var archiveException = Assert.Throws<InvalidOperationException>(() => service.ArchiveRun("missing-run"));
            var deleteException = Assert.Throws<InvalidOperationException>(() => service.DeleteRun("missing-run"));

            Assert.That(markReadException.Message, Is.EqualTo(expectedMessage));
            Assert.That(archiveException.Message, Is.EqualTo(expectedMessage));
            Assert.That(deleteException.Message, Is.EqualTo(expectedMessage));
        }

        private sealed class InMemoryAutomationStore : IAutomationStore
        {
            public AutomationStateSnapshot Snapshot = new AutomationStateSnapshot();

            public AutomationStateSnapshot Load()
            {
                return Snapshot;
            }

            public void Save(AutomationStateSnapshot snapshot)
            {
                Snapshot = snapshot;
            }

            public AutomationStateSnapshot Mutate(Func<AutomationStateSnapshot, AutomationStateSnapshot> mutation)
            {
                Snapshot = mutation(Snapshot) ?? throw new InvalidOperationException();
                return Snapshot;
            }
        }
    }
}
