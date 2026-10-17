using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationDomainSnapshotTests
    {
        [Test]
        public void AutomationStateSnapshot_RoundTripsDefinitionAndRuns()
        {
            var snapshot = new AutomationStateSnapshot
            {
                Definitions = new List<AutomationDefinitionSnapshot>
                {
                    new AutomationDefinitionSnapshot
                    {
                        Id = "automation-1",
                        Name = "Daily news",
                        Description = "Summarize daily AI news.",
                        Kind = AutomationKind.Standalone,
                        Enabled = true,
                        Prompt = "Each run, summarize important AI news.",
                        InputItems = new List<AutomationInputItemSnapshot>
                        {
                            new AutomationInputItemSnapshot
                            {
                                Type = AutomationInputItemKind.Skill,
                                Name = "news-digest",
                                DisplayName = "News Digest",
                                Path = @"E:\MyCodex\.codex\skills\news-digest"
                            },
                            new AutomationInputItemSnapshot
                            {
                                Type = AutomationInputItemKind.Plugin,
                                Name = "mycodex-office",
                                DisplayName = "MyCodex Office",
                                Path = @"E:\MyCodex\plugins\mycodex-office"
                            }
                        },
                        ModelId = "gpt-5.5",
                        ReasoningEffort = "high",
                        ApprovalMode = ApprovalMode.ReadOnly,
                        AllowNoFindingsAutoArchive = true,
                        CreatedAt = new DateTime(2026, 6, 5, 8, 0, 0, DateTimeKind.Local),
                        UpdatedAt = new DateTime(2026, 6, 5, 8, 1, 0, DateTimeKind.Local),
                        LastRunAt = new DateTime(2026, 6, 5, 8, 2, 0, DateTimeKind.Local),
                        NextRunAt = new DateTime(2026, 6, 6, 8, 0, 0, DateTimeKind.Local),
                        Schedule = new AutomationScheduleSnapshot
                        {
                            Kind = AutomationScheduleKind.Daily,
                            TimeOfDayMinutes = 8 * 60,
                            SkipMissedRuns = true
                        },
                        Context = new AutomationContextSnapshot
                        {
                            InteractionMode = AppInteractionMode.Coding,
                            RuntimeProfileId = "windows",
                            WorkspaceId = "workspace-1",
                            WorkspacePath = @"E:\MyCodex"
                        }
                    }
                },
                Runs = new List<AutomationRunSnapshot>
                {
                    new AutomationRunSnapshot
                    {
                        Id = "run-1",
                        AutomationId = "automation-1",
                        Status = AutomationRunStatus.Completed,
                        TriggerReason = AutomationTriggerReason.Scheduled,
                        ScheduledFor = new DateTime(2026, 6, 5, 8, 0, 0, DateTimeKind.Local),
                        StartedAt = new DateTime(2026, 6, 5, 8, 0, 1, DateTimeKind.Local),
                        CompletedAt = new DateTime(2026, 6, 5, 8, 2, 0, DateTimeKind.Local),
                        ThreadId = "thread-1",
                        TurnId = "turn-1",
                        WorkspaceId = "workspace-1",
                        WorkspacePath = @"E:\MyCodex",
                        ConfiguredModelId = "gpt-5.5",
                        ConfiguredReasoningEffort = "high",
                        ActualModelId = "deepseek-chat",
                        ActualReasoningEffort = null,
                        ModelResolutionMessage = "Configured model gpt-5.5 is unavailable. This run used deepseek-chat.",
                        Summary = "Found three important items.",
                        HasFindings = true,
                        NeedsAttention = true,
                        Unread = true,
                        Archived = false,
                        GeneratedArtifacts = new List<string> { @"E:\MyCodex\report.md" },
                        ChangedFiles = new List<string> { "src/demo.cs" }
                    }
                }
            };

            var reloaded = RoundTrip(snapshot);

            Assert.That(reloaded.Definitions, Has.Count.EqualTo(1));
            Assert.That(reloaded.Definitions[0].Kind, Is.EqualTo(AutomationKind.Standalone));
            Assert.That(reloaded.Definitions[0].Schedule.Kind, Is.EqualTo(AutomationScheduleKind.Daily));
            Assert.That(reloaded.Definitions[0].Context.InteractionMode, Is.EqualTo(AppInteractionMode.Coding));
            Assert.That(reloaded.Definitions[0].AllowNoFindingsAutoArchive, Is.True);
            Assert.That(reloaded.Definitions[0].InputItems, Has.Count.EqualTo(2));
            Assert.That(reloaded.Definitions[0].InputItems[0].Type, Is.EqualTo(AutomationInputItemKind.Skill));
            Assert.That(reloaded.Definitions[0].InputItems[0].Name, Is.EqualTo("news-digest"));
            Assert.That(reloaded.Definitions[0].InputItems[0].DisplayName, Is.EqualTo("News Digest"));
            Assert.That(reloaded.Definitions[0].InputItems[0].Path, Is.EqualTo(@"E:\MyCodex\.codex\skills\news-digest"));
            Assert.That(reloaded.Definitions[0].InputItems[1].Type, Is.EqualTo(AutomationInputItemKind.Plugin));
            Assert.That(reloaded.Definitions[0].InputItems[1].Name, Is.EqualTo("mycodex-office"));
            Assert.That(reloaded.Definitions[0].InputItems[1].DisplayName, Is.EqualTo("MyCodex Office"));
            Assert.That(reloaded.Definitions[0].InputItems[1].Path, Is.EqualTo(@"E:\MyCodex\plugins\mycodex-office"));
            Assert.That(reloaded.Runs, Has.Count.EqualTo(1));
            Assert.That(reloaded.Runs[0].Status, Is.EqualTo(AutomationRunStatus.Completed));
            Assert.That(reloaded.Runs[0].NeedsAttention, Is.True);
            Assert.That(reloaded.Runs[0].ConfiguredModelId, Is.EqualTo("gpt-5.5"));
            Assert.That(reloaded.Runs[0].ActualModelId, Is.EqualTo("deepseek-chat"));
            Assert.That(reloaded.Runs[0].ModelResolutionMessage, Does.Contain("deepseek-chat"));
            Assert.That(reloaded.Runs[0].GeneratedArtifacts[0], Is.EqualTo(@"E:\MyCodex\report.md"));
        }

        [Test]
        public void AutomationStateSnapshot_DefaultListsAreNonNull()
        {
            var snapshot = new AutomationStateSnapshot();

            Assert.That(snapshot.Definitions, Is.Not.Null);
            Assert.That(snapshot.Runs, Is.Not.Null);
            Assert.That(new AutomationDefinitionSnapshot().InputItems, Is.Not.Null);
        }

        private static AutomationStateSnapshot RoundTrip(AutomationStateSnapshot snapshot)
        {
            var serializer = new DataContractJsonSerializer(typeof(AutomationStateSnapshot));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, snapshot);
                var json = Encoding.UTF8.GetString(stream.ToArray());
                using (var readStream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return (AutomationStateSnapshot)serializer.ReadObject(readStream);
                }
            }
        }
    }
}
