using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TurnProgressSummaryReducerTests
    {
        [Test]
        public void ApplyEvent_TurnPlanUpdatedBuildsPlanSummary()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();

                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("turn_started", turnId: "turn-1")), Is.True);
                Assert.That(
                    TurnProgressSummaryReducer.ApplyEvent(
                        state,
                        Event(
                            "turn_plan_updated",
                            explanation: "Refactor progress state.",
                            planSteps: new[]
                            {
                                new AppServerTurnPlanStep { Step = "Inspect current flow", Status = "completed" },
                                new AppServerTurnPlanStep { Step = "Implement reducer", Status = "in_progress" },
                                new AppServerTurnPlanStep { Step = "Run tests", Status = "pending" }
                            })),
                    Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(state.TurnId, Is.EqualTo("turn-1"));
                Assert.That(state.PlanSteps, Has.Count.EqualTo(3));
                Assert.That(summary, Does.Contain("Current step: Implement reducer"));
                Assert.That(summary, Does.Contain("Plan:"));
                Assert.That(summary, Does.Contain("Completed:Inspect current flow"));
                Assert.That(summary, Does.Contain("In Progress:Implement reducer"));
                Assert.That(summary, Does.Contain("Pending:Run tests"));
            }
        }

        [Test]
        public void BuildSummaryText_TruncatesStreamingPlanDelta()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();
                var longPlan = string.Join(
                    Environment.NewLine,
                    Enumerable.Range(1, 40).Select(index => "Plan step " + index + " " + new string('x', 80)));

                Assert.That(
                    TurnProgressSummaryReducer.ApplyEvent(
                        state,
                        Event("plan_delta", planDeltaText: longPlan)),
                    Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(summary.Length, Is.LessThan(2000));
                Assert.That(summary, Does.Contain("Plan step 1"));
                Assert.That(summary, Does.Not.Contain("Plan step 40"));
                Assert.That(summary, Does.Contain("..."));
            }
        }

        [Test]
        public void PrepareForSend_WithSkills_PreservesVisibleSkillNamesForStartedTurn()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();

                TurnProgressSummaryReducer.PrepareForSend(
                    state,
                    new[] { "Repo Skill", "User Skill", "Repo Skill" });

                Assert.That(state.SkillNames, Is.EqualTo(new[] { "Repo Skill", "User Skill" }));
                Assert.That(state.PendingSkillNames, Is.EqualTo(new[] { "Repo Skill", "User Skill" }));

                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("turn_started", turnId: "turn-2")), Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(state.TurnId, Is.EqualTo("turn-2"));
                Assert.That(state.SkillNames, Is.EqualTo(new[] { "Repo Skill", "User Skill" }));
                Assert.That(state.PendingSkillNames, Is.Empty);
                Assert.That(summary, Does.Contain("Current step: Starting a new conversation turn"));
                Assert.That(summary, Does.Contain("Skills:"));
                Assert.That(summary, Does.Contain("- Repo Skill"));
                Assert.That(summary, Does.Contain("- User Skill"));

                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("turn_started", turnId: "turn-3")), Is.True);
                Assert.That(state.SkillNames, Is.Empty);
            }
        }

        [Test]
        public void ApplyEvent_ReasoningAndFileChangeEventsUpdateSummary()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();

                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("reasoning_started")), Is.True);
                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("reasoning_summary_delta", "Checking event flow.")), Is.True);
                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("reasoning_text_delta", "Latest internal note.")), Is.True);
                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("file_change_progress", "Collecting modified files.")), Is.True);
                Assert.That(
                    TurnProgressSummaryReducer.ApplyEvent(
                        state,
                        Event(
                            "file_change_completed",
                            content: "Applied reducer extraction.",
                            details: "Updated summary state handling.",
                            changedFiles: new[] { "src/MainForm.cs", "src/TurnProgressSummaryReducer.cs" })),
                    Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(state.IsReasoningActive, Is.True);
                Assert.That(state.IsFileChangeActive, Is.False);
                Assert.That(state.ChangedFiles, Is.EqualTo(new[] { "src/MainForm.cs", "src/TurnProgressSummaryReducer.cs" }));
                Assert.That(summary, Does.Contain("Thinking:"));
                Assert.That(summary, Does.Contain("Checking event flow."));
                Assert.That(summary, Does.Contain("Change details:"));
                Assert.That(summary, Does.Contain("Updated summary state handling."));
                Assert.That(summary, Does.Contain("Modified 2 file(s)"));
            }
        }

        [Test]
        public void ApplyEvent_FileChangePatchUpdatedKeepsFileChangeActive()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();

                Assert.That(
                    TurnProgressSummaryReducer.ApplyEvent(
                        state,
                        Event(
                            "file_change_patch_updated",
                            content: "Processing src/App.cs",
                            details: "Modified src/App.cs",
                            changedFiles: new[] { "src/App.cs" })),
                    Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(state.IsFileChangeActive, Is.True);
                Assert.That(state.IsFinalized, Is.False);
                Assert.That(state.FileChangeDetailText, Is.EqualTo("Modified src/App.cs"));
                Assert.That(state.ChangedFiles, Is.EqualTo(new[] { "src/App.cs" }));
                Assert.That(summary, Does.Contain("Current step: Collecting file changes"));
                Assert.That(summary, Does.Contain("Change details:"));
                Assert.That(summary, Does.Contain("Modified src/App.cs"));
            }
        }

        [Test]
        public void BuildSummaryText_ChineseFileChangeTextUsesChangeTerminology()
        {
            using (UseCulture("zh-CN"))
            {
                var state = new TurnProgressSummaryState();

                Assert.That(
                    TurnProgressSummaryReducer.ApplyEvent(
                        state,
                        Event(
                            "file_change_patch_updated",
                            details: "已修改 src/App.cs",
                            changedFiles: new[] { "src/App.cs", "tests/AppTests.cs" })),
                    Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(summary, Does.Contain("当前步骤：正在整理文件变更"));
                Assert.That(summary, Does.Contain("变更细节："));
                Assert.That(summary, Does.Contain("变更："));
                Assert.That(summary, Does.Contain("已变更 2 个文件"));
                Assert.That(summary, Does.Not.Contain("文件修改"));
                Assert.That(summary, Does.Not.Contain("改动"));
            }
        }

        [Test]
        public void ApplyEvent_ReasoningCompletedKeepsCompletedSummaryList()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();

                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("reasoning_started", itemId: "reason-1")), Is.True);
                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("reasoning_summary_delta", "**Inspecting files**  Reading entry points.", itemId: "reason-1")), Is.True);
                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("reasoning_completed", "**Inspecting files**  Reading entry points.", itemId: "reason-1")), Is.True);
                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("reasoning_completed", "Analysis completed", itemId: "placeholder")), Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(state.IsReasoningActive, Is.False);
                Assert.That(state.ReasoningSummaryText, Is.Empty);
                Assert.That(state.CompletedReasoningSummaries, Has.Count.EqualTo(1));
                Assert.That(summary, Does.Contain("Thinking:"));
                Assert.That(summary, Does.Contain("Completed: Inspecting files: Reading entry points."));
                Assert.That(summary, Does.Not.Contain("Analysis completed"));
            }
        }

        [Test]
        public void BuildSummaryText_CompletedReasoningSummaryKeepsFullTextAsSingleRecord()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();
                var longReasoningSummary =
                    "**Inspecting encoding**  " +
                    "I am wondering if the mojibake is due to encoding problems with documents. " +
                    "It seems like AGENTS suggests Chinese resources and the PowerShell default encoding may be involved. " +
                    Environment.NewLine +
                    "I should keep the full completed reasoning summary available for manual browsing.";

                Assert.That(
                    TurnProgressSummaryReducer.ApplyEvent(
                        state,
                        Event("reasoning_completed", longReasoningSummary, itemId: "reason-long")),
                    Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);
                var activityItems = ActivityDisplayState.BuildStableItems(summary, "No activity records currently.");

                Assert.That(summary, Does.Contain("Inspecting encoding: I am wondering if the mojibake"));
                Assert.That(summary, Does.Contain("I should keep the full completed reasoning summary available for manual browsing."));
                Assert.That(summary, Does.Not.Contain("..."));
                Assert.That(summary, Does.Not.Contain("browsing." + Environment.NewLine));
                Assert.That(activityItems, Has.Length.EqualTo(1));
                Assert.That(activityItems[0], Does.Contain("I should keep the full completed reasoning summary available for manual browsing."));
                Assert.That(activityItems[0], Does.Not.Contain("..."));
            }
        }

        [Test]
        public void ApplyEvent_ReasoningCompletedSummariesAreBounded()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();

                for (var i = 1; i <= 13; i++)
                {
                    Assert.That(
                        TurnProgressSummaryReducer.ApplyEvent(
                            state,
                            Event("reasoning_completed", "**Step " + i + "**  Summary " + i, itemId: "reason-" + i)),
                        Is.True);
                }

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(state.CompletedReasoningSummaries, Has.Count.EqualTo(12));
                Assert.That(summary, Does.Not.Contain("Step 1: Summary 1"));
                Assert.That(summary, Does.Contain("Step 2: Summary 2"));
                Assert.That(summary, Does.Contain("Step 13: Summary 13"));
            }
        }

        [Test]
        public void ApplyEvent_McpToolEventsKeepLatestThreeAndFinalize()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();

                for (var i = 1; i <= 4; i++)
                {
                    Assert.That(
                        TurnProgressSummaryReducer.ApplyEvent(
                            state,
                            Event(
                                "mcp_tool_progress",
                                content: "Running",
                                itemId: "item-" + i,
                                mcpServerName: "server",
                                mcpToolName: "tool-" + i,
                                itemStatus: "running")),
                        Is.True);
                }

                Assert.That(
                    TurnProgressSummaryReducer.ApplyEvent(
                        state,
                        Event(
                            "mcp_tool_completed",
                            itemId: "item-4",
                            mcpServerName: "server",
                            mcpToolName: "tool-4",
                            itemStatus: "completed")),
                    Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(state.McpToolProgressItems, Has.Count.EqualTo(3));
                Assert.That(state.McpToolProgressItems[0].ItemId, Is.EqualTo("item-2"));
                Assert.That(state.McpToolProgressItems[2].ItemId, Is.EqualTo("item-4"));
                Assert.That(state.McpToolProgressItems[2].IsFinalized, Is.True);
                Assert.That(summary, Does.Contain("MCP:"));
                Assert.That(summary, Does.Contain("server / tool-2"));
                Assert.That(summary, Does.Contain("server / tool-4"));
            }
        }

        [Test]
        public void ApplyEvent_TurnFailedFinalizesStatus()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();

                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("turn_failed", "boom")), Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(state.IsFinalized, Is.True);
                Assert.That(state.StatusText, Is.EqualTo("Turn failed: boom"));
                Assert.That(summary, Does.Contain("Current step: Turn failed: boom"));
            }
        }

        [Test]
        public void ApplyEvent_TurnStartedShowsSimpleNewTurnStatus()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();

                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("turn_started")), Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(summary, Does.Contain("Current step: Starting a new conversation turn"));
            }
        }

        [Test]
        public void ApplyEvent_TokenUsageAfterTurnCompleted_AppendsLastAndTotalUsageToCompletedStatus()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();

                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("turn_completed")), Is.True);
                Assert.That(
                    TurnProgressSummaryReducer.ApplyEvent(
                        state,
                        TokenUsageEvent(lastTotalTokens: 23456, totalTokens: 123456789)),
                    Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(summary, Does.Contain("Current step: Current turn completed | This turn 2.35万 | Total 1.23亿"));
            }
        }

        [Test]
        public void ApplyEvent_TokenUsageBeforeTurnCompleted_KeepsUsageOnCompletedStatus()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();

                Assert.That(
                    TurnProgressSummaryReducer.ApplyEvent(
                        state,
                        TokenUsageEvent(lastTotalTokens: 9000, totalTokens: 25000)),
                    Is.True);
                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("turn_completed")), Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(summary, Does.Contain("Current step: Current turn completed | This turn 9,000 | Total 2.5万"));
            }
        }

        [Test]
        public void ApplyEvent_StaleTokenUsageForPreviousTurn_DoesNotUpdateCurrentTurnSummary()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();

                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("turn_started", turnId: "turn-2")), Is.True);
                Assert.That(
                    TurnProgressSummaryReducer.ApplyEvent(
                        state,
                        TokenUsageEvent(lastTotalTokens: 23456, totalTokens: 123456789, turnId: "turn-1")),
                    Is.False);
                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("turn_completed", turnId: "turn-2")), Is.True);

                var summary = TurnProgressSummaryReducer.BuildSummaryText(state);

                Assert.That(summary, Does.Contain("Current step: Current turn completed"));
                Assert.That(summary, Does.Not.Contain("This turn"));
                Assert.That(summary, Does.Not.Contain("Total 1.23亿"));
            }
        }

        [Test]
        public void FinalizeInterrupted_UsesReasonAndStopsActiveProgress()
        {
            using (UseCulture("en-US"))
            {
                var state = new TurnProgressSummaryState();
                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("reasoning_summary_delta", "Analyzing")), Is.True);
                Assert.That(TurnProgressSummaryReducer.ApplyEvent(state, Event("file_change_progress", "Updating files")), Is.True);
                state.PendingSkillNames.Add("Repo Skill");

                TurnProgressSummaryReducer.FinalizeInterrupted(state, "user cancelled");

                Assert.That(state.IsReasoningActive, Is.False);
                Assert.That(state.IsFileChangeActive, Is.False);
                Assert.That(state.IsFinalized, Is.True);
                Assert.That(state.PendingSkillNames, Is.Empty);
                Assert.That(TurnProgressSummaryReducer.BuildSummaryText(state), Does.Contain("Current step: Turn interrupted: user cancelled"));
            }
        }

        private static AppServerEventArgs Event(
            string eventType,
            string content = null,
            string turnId = "turn-1",
            string itemId = null,
            string explanation = null,
            IReadOnlyList<AppServerTurnPlanStep> planSteps = null,
            IReadOnlyList<string> changedFiles = null,
            string details = null,
            string mcpServerName = null,
            string mcpToolName = null,
            string itemStatus = null,
            string planDeltaText = null)
        {
            return new AppServerEventArgs(
                eventType,
                content,
                threadId: "thread-1",
                turnId: turnId,
                itemId: itemId,
                explanation: explanation,
                planSteps: planSteps,
                changedFiles: changedFiles,
                details: details,
                mcpServerName: mcpServerName,
                mcpToolName: mcpToolName,
                itemStatus: itemStatus,
                planDeltaText: planDeltaText);
        }

        private static AppServerEventArgs TokenUsageEvent(long lastTotalTokens, long totalTokens, string turnId = "turn-1")
        {
            var usage = new AppServerThreadTokenUsageSnapshot
            {
                Total = new AppServerTokenUsageBreakdown
                {
                    TotalTokens = totalTokens
                },
                Last = new AppServerTokenUsageBreakdown
                {
                    TotalTokens = lastTotalTokens
                }
            };

            return new AppServerEventArgs(
                "token_usage",
                "Context usage updated.",
                threadId: "thread-1",
                turnId: turnId,
                totalTokens: totalTokens,
                threadTokenUsage: usage);
        }

        private static IDisposable UseCulture(string cultureName)
        {
            return new CultureScope(cultureName);
        }

        private sealed class CultureScope : IDisposable
        {
            private readonly CultureInfo _originalCulture;
            private readonly CultureInfo _originalUiCulture;
            private readonly CultureInfo _originalDefaultCulture;
            private readonly CultureInfo _originalDefaultUiCulture;

            public CultureScope(string cultureName)
            {
                _originalCulture = CultureInfo.CurrentCulture;
                _originalUiCulture = CultureInfo.CurrentUICulture;
                _originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
                _originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }

            public void Dispose()
            {
                CultureInfo.CurrentCulture = _originalCulture;
                CultureInfo.CurrentUICulture = _originalUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = _originalDefaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultUiCulture;
            }
        }
    }
}
