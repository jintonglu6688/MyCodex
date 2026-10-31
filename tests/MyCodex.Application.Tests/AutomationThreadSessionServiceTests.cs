using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationThreadSessionServiceTests
    {
        [TestCase("windows", AppInteractionMode.Coding)]
        [TestCase("wsl|ubuntu", AppInteractionMode.Coding)]
        [TestCase("remote-mac|server", AppInteractionMode.Office)]
        [TestCase("remote-unix|server", AppInteractionMode.Coding)]
        public void InitializeModelSettings_BindsActualModelIndependentlyOfChangedDefaults(string profile, AppInteractionMode mode)
        {
            var snapshot = Snapshot(profile, mode);
            var session = new ChatSession { ThreadId = "thread", RuntimeProfileId = profile, Mode = mode };
            Assert.That(AutomationThreadSessionService.InitializeModelSettings(new[] { session }, snapshot), Is.True);
            Assert.That(session.AutomationModelSettingsInitialized, Is.True);
            var resolved = SessionDefaultsService.ResolveSession(
                new SessionDefaultsResolution { ModelId = "gpt-6.1-sol", ReasoningEffort = "medium" }, session, null);
            Assert.That(resolved.ModelId, Is.EqualTo("gpt-5.6-luna"));
            Assert.That(resolved.ReasoningEffort, Is.EqualTo("high"));
            snapshot.Runs[0].ActualModelId = "changed-task-model";
            Assert.That(AutomationThreadSessionService.InitializeModelSettings(new[] { session }, snapshot), Is.False);
            Assert.That(session.ModelId, Is.EqualTo("gpt-5.6-luna"));
        }

        [TestCase(null, null)]
        [TestCase("manual-model", "low")]
        public void InitializeModelSettings_NeverReappliesAfterManualResetOrModelChange(string model, string effort)
        {
            var snapshot = Snapshot();
            var session = new ChatSession { ThreadId = "thread" };
            AutomationThreadSessionService.InitializeModelSettings(new[] { session }, snapshot);
            session.ModelId = model;
            session.ReasoningEffort = effort;
            Assert.That(AutomationThreadSessionService.InitializeModelSettings(new[] { session }, snapshot), Is.False);
            var resolved = SessionDefaultsService.ResolveSession(
                new SessionDefaultsResolution { ModelId = "global", ReasoningEffort = "medium" }, session, null);
            Assert.That(resolved.ModelId, Is.EqualTo(model ?? "global"));
            Assert.That(resolved.ReasoningEffort, Is.EqualTo(effort ?? "medium"));
        }

        [Test]
        public void InitializeModelSettings_KeepsFollowDefaultEvenAfterTaskSettingsChange()
        {
            var snapshot = Snapshot();
            snapshot.Runs[0].ConfiguredModelId = null;
            snapshot.Runs[0].ConfiguredReasoningEffort = null;
            var session = new ChatSession { ThreadId = "thread" };
            Assert.That(AutomationThreadSessionService.InitializeModelSettings(new[] { session }, snapshot), Is.True);
            Assert.That(session.AutomationModelSettingsInitialized, Is.True);
            Assert.That(session.ModelId, Is.Null);
            Assert.That(session.ReasoningEffort, Is.Null);
            snapshot.Definitions[0].ModelId = "changed-task-model";
            snapshot.Definitions[0].ReasoningEffort = "high";
            snapshot.Runs[0].ConfiguredModelId = "changed-task-model";
            Assert.That(AutomationThreadSessionService.InitializeModelSettings(new[] { session }, snapshot), Is.False);
            var resolved = SessionDefaultsService.ResolveSession(
                new SessionDefaultsResolution { ModelId = "new-default", ReasoningEffort = "low" }, session, null);
            Assert.That(resolved.ModelId, Is.EqualTo("new-default"));
            Assert.That(resolved.ReasoningEffort, Is.EqualTo("low"));
        }

        [Test]
        public void InitializeModelSettings_PreservesAnExistingManualOverrideDuringLegacyRecovery()
        {
            var session = new ChatSession { ThreadId = "thread", ModelId = "manual", ReasoningEffort = "low" };
            AutomationThreadSessionService.InitializeModelSettings(new[] { session }, Snapshot());
            Assert.That(session.ModelId, Is.EqualTo("manual"));
            Assert.That(session.ReasoningEffort, Is.EqualTo("low"));
            Assert.That(session.AutomationModelSettingsInitialized, Is.True);
        }

        [TestCase(AutomationKind.Thread, "thread", "windows", AppInteractionMode.Coding)]
        [TestCase(AutomationKind.Standalone, "another-thread", "windows", AppInteractionMode.Coding)]
        [TestCase(AutomationKind.Standalone, "thread", "wsl|another", AppInteractionMode.Coding)]
        [TestCase(AutomationKind.Standalone, "thread", "windows", AppInteractionMode.Office)]
        public void InitializeModelSettings_DoesNotChangeUnrelatedConversations(AutomationKind kind, string thread, string profile, AppInteractionMode mode)
        {
            var snapshot = Snapshot();
            snapshot.Definitions[0].Kind = kind;
            var session = new ChatSession { ThreadId = thread, RuntimeProfileId = profile, Mode = mode };
            Assert.That(AutomationThreadSessionService.InitializeModelSettings(new[] { session }, snapshot), Is.False);
            Assert.That(session.ModelId, Is.Null);
            Assert.That(session.AutomationModelSettingsInitialized, Is.False);
        }

        [Test]
        public void InitializeModelSettings_UsesActualResolutionAndDoesNotInventReasoningEffort()
        {
            var snapshot = Snapshot();
            snapshot.Runs[0].ActualModelId = "fallback-model";
            snapshot.Runs[0].ActualReasoningEffort = null;
            var session = new ChatSession { ThreadId = "thread" };
            AutomationThreadSessionService.InitializeModelSettings(new[] { session }, snapshot);
            Assert.That(session.ModelId, Is.EqualTo("fallback-model"));
            Assert.That(session.ReasoningEffort, Is.Null);
        }

        [Test]
        public void InitializeModelSettings_WaitsForActualResolutionAndPreservesOrdinaryDefaultFollowing()
        {
            var snapshot = Snapshot();
            snapshot.Runs[0].ActualModelId = null;
            var session = new ChatSession { ThreadId = "thread" };
            Assert.That(AutomationThreadSessionService.InitializeModelSettings(new[] { session }, snapshot), Is.False);
            Assert.That(session.AutomationModelSettingsInitialized, Is.False);
            Assert.That(AutomationThreadSessionService.InitializeModelSettings(new[] { session }, null), Is.False);
        }

        [Test]
        public void BuildTitleMap_KeepsStandaloneNamesAndIgnoresAttachedThreadAutomations()
        {
            var snapshot = Snapshot();
            snapshot.Definitions[0].Name = " Apple news ";
            snapshot.Definitions.Add(new AutomationDefinitionSnapshot { Id = "attached", Name = "Do not rename", Kind = AutomationKind.Thread });
            snapshot.Runs.Add(new AutomationRunSnapshot { AutomationId = "attached", ThreadId = "attached-thread" });
            var titles = AutomationThreadSessionService.BuildTitleMap(snapshot);
            Assert.That(titles["thread"], Is.EqualTo("Apple news"));
            Assert.That(titles.ContainsKey("attached-thread"), Is.False);
            Assert.That(AutomationThreadSessionService.BuildTitleMap(null), Is.Empty);
        }

        [Test]
        public void BuildTitleMap_IgnoresNamelessDuplicateBeforeChoosingTheOriginalFallback()
        {
            var snapshot = Snapshot();
            snapshot.Definitions.Insert(0, new AutomationDefinitionSnapshot { Id = "automation", Kind = AutomationKind.Standalone });
            Assert.That(AutomationThreadSessionService.BuildTitleMap(snapshot)["thread"], Is.EqualTo("News"));
        }

        [Test]
        public void InitializeModelSettings_DoesNotBindAProjectAutomationThatReusesAnExistingThread()
        {
            var snapshot = Snapshot();
            snapshot.Definitions[0].Kind = AutomationKind.Project;
            snapshot.Definitions[0].Context.ThreadId = "thread";
            Assert.That(AutomationThreadSessionService.InitializeModelSettings(new[] { new ChatSession { ThreadId = "thread" } }, snapshot), Is.False);
        }

        private static AutomationStateSnapshot Snapshot(string profile = "windows", AppInteractionMode mode = AppInteractionMode.Coding)
        {
            return new AutomationStateSnapshot
            {
                Definitions = new List<AutomationDefinitionSnapshot>
                {
                    new AutomationDefinitionSnapshot
                    {
                        Id = "automation", Name = "News", Kind = AutomationKind.Standalone,
                        Context = new AutomationContextSnapshot { RuntimeProfileId = profile, InteractionMode = mode }
                    }
                },
                Runs = new List<AutomationRunSnapshot>
                {
                    new AutomationRunSnapshot
                    {
                        Id = "run", AutomationId = "automation", ThreadId = "thread", StartedAt = new DateTime(2026, 10, 31, 9, 0, 0),
                        ConfiguredModelId = "gpt-5.6-luna", ConfiguredReasoningEffort = "high",
                        ActualModelId = "gpt-5.6-luna", ActualReasoningEffort = "high"
                    }
                }
            };
        }
    }
}
