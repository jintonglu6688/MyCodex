using System;
using System.Globalization;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionDisplayFormatterTests
    {
        [TestCase(SessionListDisplayOrder.AgentsTimeTitle, "en-US", "2 subagent(s), 2 hour(s) ago, Mainline: Work item [Plan] [Goal]")]
        [TestCase(SessionListDisplayOrder.TitleTimeAgents, "en-US", "Mainline: Work item [Plan] [Goal], 2 hour(s) ago, 2 subagent(s)")]
        [TestCase(SessionListDisplayOrder.TitleAgentsTime, "en-US", "Mainline: Work item [Plan] [Goal], 2 subagent(s), 2 hour(s) ago")]
        [TestCase(SessionListDisplayOrder.AgentsTimeTitle, "zh-CN", "有 2 个子代理，2小时前，主线：Work item [计划] [目标]")]
        [TestCase(SessionListDisplayOrder.TitleTimeAgents, "zh-CN", "主线：Work item [计划] [目标]，2小时前，有 2 个子代理")]
        [TestCase(SessionListDisplayOrder.TitleAgentsTime, "zh-CN", "主线：Work item [计划] [目标]，有 2 个子代理，2小时前")]
        public void SessionListItem_OrdersInformationAndKeepsStatusMarkers(string order, string culture, string expected)
        {
            using (UseCulture(culture))
            {
                var session = new ChatSession
                {
                    Title = "Work item",
                    CreatedAt = DateTime.Now.AddHours(-2.5),
                    IsPlanModeEnabled = true,
                    ThreadGoalObjective = "Ship the feature"
                };
                var presenter = new SessionListItem(new SessionListEntry(session,
                    new[] { new ChatSession(), new ChatSession() }), true, true, order);
                var item = presenter.CreateListViewItem();
                Assert.That(item.Text, Is.EqualTo(CurrentListItemMarker.Append(
                    SessionDisplayFormatter.PrependWorkingStatus(expected, 1), true)));
                Assert.That(item.ToolTipText, Is.EqualTo(item.Text));

                presenter.UpdateWorkingState(item, false);
                Assert.That(item.Text, Is.EqualTo(CurrentListItemMarker.Append(expected, true)));
                Assert.That(item.ToolTipText, Is.EqualTo(item.Text));
            }
        }

        [TestCase(SessionListDisplayOrder.AgentsTimeTitle, "2 hour(s) ago, Mainline: Work item", "1 subagent(s), Mainline: Work item")]
        [TestCase(SessionListDisplayOrder.TitleTimeAgents, "Mainline: Work item, 2 hour(s) ago", "Mainline: Work item, 1 subagent(s)")]
        [TestCase(SessionListDisplayOrder.TitleAgentsTime, "Mainline: Work item, 2 hour(s) ago", "Mainline: Work item, 1 subagent(s)")]
        public void SessionListItem_OmitsAbsentInformationWithoutExtraSeparators(string order, string withoutAgents, string withoutTime)
        {
            using (UseCulture("en-US"))
            {
                var session = new ChatSession { Title = "Work item", CreatedAt = DateTime.Now.AddHours(-2.5) };
                Assert.That(new SessionListItem(new SessionListEntry(session, null), false, false, order).ToString(), Is.EqualTo(withoutAgents));
                session.CreatedAt = default(DateTime);
                Assert.That(new SessionListItem(new SessionListEntry(session, new[] { new ChatSession() }), false, false, order).ToString(), Is.EqualTo(withoutTime));
                Assert.That(new SessionListItem(new SessionListEntry(session, null), false, false, order).ToString(), Is.EqualTo("Mainline: Work item"));
            }
        }

        [Test]
        public void BuildDisplayTitle_UsesTypeTitleFormatAndNormalizesLegacyPrefix()
        {
            using (UseCulture("en-US"))
            {
                Assert.That(
                    SessionDisplayFormatter.BuildDisplayTitle(new ChatSession { Title = "Work item" }),
                    Is.EqualTo("Mainline: Work item"));
                Assert.That(
                    SessionDisplayFormatter.BuildDisplayTitle(new ChatSession { Title = "Mainline | Work item" }),
                    Is.EqualTo("Mainline: Work item"));
                Assert.That(
                    SessionDisplayFormatter.BuildDisplayTitle(new ChatSession { Title = " " }),
                    Is.EqualTo("Mainline: Unnamed session"));
            }
        }

        [Test]
        public void BuildDisplayTitle_ForForkRemovesVisibleForkNumbers()
        {
            using (UseCulture("en-US"))
            {
                Assert.That(
                    SessionDisplayFormatter.BuildDisplayTitle(new ChatSession { IsFork = true, ForkIndex = 1, Title = "Source title - Fork 1" }),
                    Is.EqualTo("Fork: Source title"));
                Assert.That(
                    SessionDisplayFormatter.BuildDisplayTitle(new ChatSession { IsFork = true, ForkIndex = 2, Title = "Fork 2 | Source title" }),
                    Is.EqualTo("Fork: Source title"));
            }
        }

        [Test]
        public void BuildDisplayTitle_AndGoalSummary_ExposeGoalState()
        {
            using (UseCulture("en-US"))
            {
                var session = new ChatSession
                {
                    Title = "Work item",
                    ThreadGoalObjective = "Ship the goal feature",
                    ThreadGoalStatus = "active",
                    ThreadGoalTokensUsed = 42,
                    ThreadGoalTokenBudget = 1000
                };

                Assert.That(
                    SessionDisplayFormatter.BuildDisplayTitle(session),
                    Is.EqualTo("Mainline: Work item [Goal]"));
                Assert.That(SessionDisplayFormatter.HasGoal(session), Is.True);
                Assert.That(
                    SessionDisplayFormatter.BuildGoalSummaryText(session),
                    Is.EqualTo("Goal: Ship the goal feature | status: Active | tokens used: 42 | token budget: 1000"));
            }
        }

        [Test]
        public void BuildBranchLabel_UsesTypeWithoutForkIndex()
        {
            using (UseCulture("en-US"))
            {
                Assert.That(SessionDisplayFormatter.BuildBranchLabel(null), Is.EqualTo("Mainline"));
                Assert.That(
                    SessionDisplayFormatter.BuildBranchLabel(new ChatSession { IsFork = true }),
                    Is.EqualTo("Fork"));
                Assert.That(
                    SessionDisplayFormatter.BuildBranchLabel(new ChatSession { IsFork = true, ForkIndex = 3 }),
                    Is.EqualTo("Fork"));
                Assert.That(
                    SessionDisplayFormatter.BuildBranchLabel(new ChatSession { IsFork = true, IsReview = true, ForkIndex = 3 }),
                    Is.EqualTo("Review"));
            }
        }

        [Test]
        public void BuildSourceText_PrefersTitlesAndFallsBackToThreadIds()
        {
            using (UseCulture("en-US"))
            {
                Assert.That(
                    SessionDisplayFormatter.BuildForkSourceText(
                        new ChatSession { IsFork = true, ForkSourceTitle = "Source title", ForkSourceThreadId = "thread-1" }),
                    Is.EqualTo("Source title"));
                Assert.That(
                    SessionDisplayFormatter.BuildForkSourceText(
                        new ChatSession { IsFork = true, ForkSourceThreadId = "thread-1" }),
                    Is.EqualTo("thread-1"));
                Assert.That(
                    SessionDisplayFormatter.BuildReviewSourceText(
                        new ChatSession { IsReview = true, ReviewSourceTitle = "Review source", ReviewSourceThreadId = "thread-2" }),
                    Is.EqualTo("Review source"));
                Assert.That(
                    SessionDisplayFormatter.BuildReviewSourceText(
                        new ChatSession { IsReview = true, ReviewSourceThreadId = "thread-2" }),
                    Is.EqualTo("thread-2"));
                Assert.That(SessionDisplayFormatter.BuildForkSourceText(new ChatSession()), Is.Empty);
                Assert.That(SessionDisplayFormatter.BuildReviewSourceText(new ChatSession()), Is.Empty);
            }
        }

        [Test]
        public void BuildReviewSessionTitle_AppendsReviewSuffixOnlyWhenMissing()
        {
            using (UseCulture("en-US"))
            {
                Assert.That(
                    SessionDisplayFormatter.BuildReviewSessionTitle(new ChatSession { Title = "Feature work" }),
                    Is.EqualTo("Feature work (Review)"));
                Assert.That(
                    SessionDisplayFormatter.BuildReviewSessionTitle(new ChatSession { Title = "Feature Review" }),
                    Is.EqualTo("Feature Review"));
                Assert.That(SessionDisplayFormatter.BuildReviewSessionTitle(null), Is.EqualTo("Unnamed session (Review)"));
            }
        }

        [Test]
        public void BuildForkTitle_UsesCleanSourceTitleWithoutForkIndex()
        {
            using (UseCulture("en-US"))
            {
                Assert.That(
                    SessionTitleStrategy.BuildForkTitle("Source title", 1),
                    Is.EqualTo("Source title"));
                Assert.That(
                    SessionTitleStrategy.BuildForkTitle("Source title - Fork 1", 2),
                    Is.EqualTo("Source title"));
                Assert.That(
                    SessionTitleStrategy.BuildForkTitle("Fork 3 | Source title - Fork 1", 4),
                    Is.EqualTo("Source title"));
            }
        }

        [Test]
        public void BuildUniqueTitle_AppendsSuffixToNewDuplicateOnly()
        {
            using (UseCulture("en-US"))
            {
                var sessions = new[]
                {
                    new ChatSession { WorkspaceId = "workspace-1", IsFork = true, Title = "Discuss" },
                    new ChatSession { WorkspaceId = "workspace-1", IsFork = true, Title = "Discuss (2)" },
                    new ChatSession { WorkspaceId = "workspace-1", Title = "Discuss" },
                    new ChatSession { WorkspaceId = "workspace-2", IsFork = true, Title = "Discuss" }
                };

                Assert.That(
                    SessionTitleStrategy.BuildUniqueTitle("Discuss", sessions, "workspace-1", true, false),
                    Is.EqualTo("Discuss (3)"));
                Assert.That(
                    sessions[0].Title,
                    Is.EqualTo("Discuss"));
                Assert.That(
                    SessionTitleStrategy.BuildUniqueTitle("Discuss", sessions, "workspace-1", false, false),
                    Is.EqualTo("Discuss (2)"));
            }
        }

        [Test]
        public void ShouldReplaceGeneratedTitle_TreatsWorkspaceNameAsDefaultTitle()
        {
            using (UseCulture("en-US"))
            {
                var workspace = new WorkspaceInfo { Name = "Demo" };

                Assert.That(
                    SessionTitleStrategy.ShouldReplaceGeneratedTitle(new ChatSession { Title = "Demo" }, workspace),
                    Is.True);
                Assert.That(
                    SessionTitleStrategy.ShouldReplaceGeneratedTitle(new ChatSession { Title = "Demo Session 3" }, workspace),
                    Is.True);
                Assert.That(
                    SessionTitleStrategy.ShouldReplaceGeneratedTitle(new ChatSession { Title = "Custom" }, workspace),
                    Is.False);
            }
        }

        [Test]
        public void GetCreatedAgoText_UsesHoursAndDaysThresholds()
        {
            using (UseCulture("en-US"))
            {
                var now = new DateTime(2026, 4, 19, 12, 0, 0);

                Assert.That(SessionDisplayFormatter.GetCreatedAgoText(default(DateTime), now), Is.Empty);
                Assert.That(SessionDisplayFormatter.GetCreatedAgoText(now.AddMinutes(-59), now), Is.Empty);
                Assert.That(SessionDisplayFormatter.GetCreatedAgoText(now.AddHours(-2.5), now), Is.EqualTo("2 hour(s) ago"));
                Assert.That(SessionDisplayFormatter.GetCreatedAgoText(now.AddDays(-3.2), now), Is.EqualTo("3 day(s) ago"));
            }
        }

        [TestCase(100000L, 20000L, 80)]
        [TestCase(100000L, 0L, 100)]
        [TestCase(100000L, 100000L, 0)]
        [TestCase(100000L, 120000L, 0)]
        [TestCase(3L, 1L, 67)]
        public void CalculateContextRemainingPercentage_UsesLastTurnUsage(
            long contextWindow,
            long lastTotalTokens,
            int expected)
        {
            var session = new ChatSession
            {
                ThreadTokenUsageModelContextWindow = contextWindow,
                ThreadTokenUsageLastTotalTokens = lastTotalTokens,
                ThreadTokenUsageTotalTokens = 999999999
            };

            Assert.That(SessionDisplayFormatter.CalculateContextRemainingPercentage(session), Is.EqualTo(expected));
        }

        [Test]
        public void CalculateContextRemainingPercentage_OmitsInvalidOrMissingUsage()
        {
            Assert.That(SessionDisplayFormatter.CalculateContextRemainingPercentage(null), Is.Null);
            Assert.That(SessionDisplayFormatter.CalculateContextRemainingPercentage(new ChatSession()), Is.Null);
            Assert.That(
                SessionDisplayFormatter.CalculateContextRemainingPercentage(new ChatSession
                {
                    ThreadTokenUsageModelContextWindow = 0,
                    ThreadTokenUsageLastTotalTokens = 0
                }),
                Is.Null);
            Assert.That(
                SessionDisplayFormatter.CalculateContextRemainingPercentage(new ChatSession
                {
                    ThreadTokenUsageModelContextWindow = 100,
                    ThreadTokenUsageLastTotalTokens = -1
                }),
                Is.Null);
        }

        [Test]
        public void WorkingText_FormatsListPrefixesAndOptionalContext()
        {
            using (UseCulture("en-US"))
            {
                Assert.That(SessionDisplayFormatter.PrependWorkingStatus("Repo", 0), Is.EqualTo("Repo"));
                Assert.That(SessionDisplayFormatter.PrependWorkingStatus("Repo", 1), Is.EqualTo("Working: Repo"));
                Assert.That(SessionDisplayFormatter.PrependWorkingStatus("Repo", 3), Is.EqualTo("3 sessions working: Repo"));
                Assert.That(SessionDisplayFormatter.BuildWorkingStatusText(new ChatSession()), Is.EqualTo("Working"));
                Assert.That(
                    SessionDisplayFormatter.BuildWorkingStatusText(new ChatSession
                    {
                        ThreadTokenUsageModelContextWindow = 100,
                        ThreadTokenUsageLastTotalTokens = 20
                    }),
                    Is.EqualTo("Working (80% context remaining)"));
            }
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
