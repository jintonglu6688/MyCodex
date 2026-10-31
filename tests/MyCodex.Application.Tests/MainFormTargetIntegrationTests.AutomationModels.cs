using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Tests.Fakes;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class MainFormTargetIntegrationTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ImportedAutomationThreadHonorsModelPolicyAndManualReset(bool previouslyImported, bool followDefault)
        {
            using (var fixture = new TargetFormFixture())
            {
                var store = new JsonAutomationStore(fixture.Paths);
                store.Save(new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation", Name = "Apple news", Kind = AutomationKind.Standalone,
                            Context = new AutomationContextSnapshot { RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId }
                        }
                    },
                    Runs = new List<AutomationRunSnapshot>
                    {
                        new AutomationRunSnapshot
                        {
                            Id = "run", AutomationId = "automation", ThreadId = "automation-thread",
                            ConfiguredModelId = followDefault ? null : "gpt-5.6-luna",
                            ConfiguredReasoningEffort = followDefault ? null : "high",
                            ActualModelId = followDefault ? "gpt-6.1-sol" : "gpt-5.6-luna",
                            ActualReasoningEffort = "high", StartedAt = new DateTime(2026, 10, 31)
                        }
                    }
                });
                FormTestDriver.SetField(fixture.Form, "_automationStore", store);
                FormTestDriver.SetField(fixture.Form, "_globalServerConfigSnapshot",
                    new AppServerConfigSnapshot { ModelId = "gpt-6.1-sol", ModelReasoningEffort = "high" });
                if (previouslyImported)
                    FormTestDriver.Field<List<ChatSession>>(fixture.Form, "_sessions").Add(
                        new ChatSession { Id = "legacy", ThreadId = "automation-thread", RuntimeProfileId = "windows", IsImported = true });
                FormTestDriver.Invoke(fixture.Form, "ImportThreads", new List<AppServerThreadSummary>
                {
                    new AppServerThreadSummary { ThreadId = "automation-thread", WorkingDirectory = fixture.Root }
                }, true);
                var session = FormTestDriver.Field<List<ChatSession>>(fixture.Form, "_sessions").Single();
                var initialModel = followDefault ? "gpt-6.1-sol" : "gpt-5.6-luna";

                Assert.That(session.ModelId, Is.EqualTo(followDefault ? null : "gpt-5.6-luna"));
                Assert.That(session.ReasoningEffort, Is.EqualTo(followDefault ? null : "high"));
                Assert.That(FormTestDriver.Invoke<string>(fixture.Form, "GetEffectiveModelId", session), Is.EqualTo(initialModel));
                Assert.That(FormTestDriver.Invoke<string>(fixture.Form, "GetEffectiveReasoningEffort", session), Is.EqualTo("high"));
                AssertContinuationModel(fixture, session, initialModel, "high");

                var display = FormTestDriver.Field<SessionHistoryDisplayBuilder>(fixture.Form, "_sessionHistoryDisplayBuilder");
                Assert.That(display.BuildSessionHeaderItems(session, false).Any(item => item.FullText.Contains(followDefault ? "6.1" : "5.6")), Is.True);
                var statistics = SessionStatisticsDetailBuilder.BuildDetailLines(session,
                    new SessionStatisticsDisplayContext { EffectiveModelId = "gpt-6.1-sol", EffectiveReasoningEffort = "medium" });
                Assert.That(statistics.Any(line => line.Contains(initialModel)), Is.True);

                FormTestDriver.SetField(fixture.Form, "_globalServerConfigSnapshot",
                    new AppServerConfigSnapshot { ModelId = "global-changed", ModelReasoningEffort = "low" });
                FormTestDriver.Invoke(fixture.Form, "ImportThreads", new List<AppServerThreadSummary>
                {
                    new AppServerThreadSummary { ThreadId = "automation-thread", WorkingDirectory = fixture.Root }
                }, true);
                var nextModel = followDefault ? "global-changed" : "gpt-5.6-luna";
                var nextEffort = followDefault ? "low" : "high";
                Assert.That(FormTestDriver.Invoke<string>(fixture.Form, "GetEffectiveModelId", session), Is.EqualTo(nextModel));
                Assert.That(FormTestDriver.Invoke<string>(fixture.Form, "GetEffectiveReasoningEffort", session), Is.EqualTo(nextEffort));
                AssertContinuationModel(fixture, session, nextModel, nextEffort);

                FormTestDriver.Invoke(fixture.Form, "RefreshSessionList");
                var list = FormTestDriver.Field<ListView>(fixture.Form, "_sessionList");
                var handle = list.Handle;
                Assert.That(list.Items.Count, Is.EqualTo(1));
                list.Items[0].Selected = true;
                Assert.That(list.SelectedItems.Count, Is.EqualTo(1));
                FormTestDriver.Invoke(fixture.Form, "SessionResetSettingsMenuItem_Click", null, EventArgs.Empty);
                Assert.That(session.ModelId, Is.Null);
                Assert.That(session.AutomationModelSettingsInitialized, Is.True);
                FormTestDriver.Invoke(fixture.Form, "RestoreAutomationSessionModelSettings", (object)new[] { session });
                Assert.That(FormTestDriver.Invoke<string>(fixture.Form, "GetEffectiveModelId", session), Is.EqualTo("global-changed"));
                AssertContinuationModel(fixture, session, "global-changed", "low");
            }
        }

        private static void AssertContinuationModel(TargetFormFixture fixture, ChatSession session, string model, string effort)
        {
            var client = new AutomationContinuationClient();
            var service = new SessionThreadService(client, new WorkspaceService())
            {
                ModelSettingsResolver = (current, workspace) => Task.FromResult(
                    FormTestDriver.Invoke<SessionDefaultsResolution>(fixture.Form, "ResolveSessionModelSettings", current))
            };
            service.SendUserMessageAsync(session, new WorkspaceInfo { RootPath = fixture.Root },
                new[] { new AppServerUserInputItem { Type = "text", Text = "Continue" } }, null, null, null).GetAwaiter().GetResult();
            Assert.That(client.ModelId, Is.EqualTo(model));
            Assert.That(client.Effort, Is.EqualTo(effort));
        }

        private sealed class AutomationContinuationClient : UnsupportedAppServerThreadClient
        {
            public string ModelId;
            public string Effort;
            public override Task SendUserMessageAsync(string threadId, string workspacePath,
                IReadOnlyCollection<AppServerUserInputItem> inputItems, ApprovalMode? approvalMode,
                string modelId, string reasoningEffort, AppServerCollaborationMode collaborationMode = null)
            {
                ModelId = modelId;
                Effort = reasoningEffort;
                return Task.CompletedTask;
            }
        }
    }
}
