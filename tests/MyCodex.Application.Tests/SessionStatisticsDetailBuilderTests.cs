using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionStatisticsDetailBuilderTests
    {
        [Test]
        public void BuildDetailLines_WithConversationUsageAndGoal_ShowsOnlyConversationStatistics()
        {
            WithCulture("en-US", () =>
            {
                var session = new ChatSession
                {
                    Title = "Fix remote SSH",
                    ThreadId = "thread-1",
                    CreatedAt = new DateTime(2026, 7, 5, 10, 0, 0),
                    LastActiveAt = new DateTime(2026, 7, 5, 11, 30, 0),
                    ModelId = "gpt-5.4",
                    ReasoningEffort = "medium",
                    ThreadTokenUsageModelContextWindow = 64000,
                    ThreadTokenUsageTotalTokens = 1234,
                    ThreadTokenUsageInputTokens = 800,
                    ThreadTokenUsageCachedInputTokens = 300,
                    ThreadTokenUsageOutputTokens = 123456789,
                    ThreadTokenUsageReasoningOutputTokens = 34,
                    ThreadTokenUsageLastTotalTokens = 20000,
                    ThreadTokenUsageLastInputTokens = 140,
                    ThreadTokenUsageLastCachedInputTokens = 60,
                    ThreadTokenUsageLastOutputTokens = 20,
                    ThreadTokenUsageLastReasoningOutputTokens = 14,
                    ThreadGoalObjective = "Finish the migration",
                    ThreadGoalStatus = "active",
                    ThreadGoalTokensUsed = 30000,
                    ThreadGoalTokenBudget = 200000,
                    ThreadGoalTimeUsedSeconds = 3661
                };

                var lines = SessionStatisticsDetailBuilder.BuildDetailLines(session);

                Assert.That(lines, Has.Some.EqualTo("Title: Mainline | Fix remote SSH"));
                Assert.That(lines, Has.Some.EqualTo("Thread: thread-1"));
                Assert.That(lines, Has.Some.EqualTo("Model: gpt-5.4"));
                Assert.That(lines, Has.Some.EqualTo("Reasoning effort: medium"));
                Assert.That(lines, Has.Some.EqualTo("Context window: 6.4万"));
                Assert.That(lines, Has.Some.EqualTo("Total tokens: 1,234"));
                Assert.That(lines, Has.Some.EqualTo("Cached input tokens: 300"));
                Assert.That(lines, Has.Some.EqualTo("Output tokens: 1.23亿"));
                Assert.That(lines, Has.Some.EqualTo("Last turn total tokens: 2万"));
                Assert.That(lines, Has.Some.EqualTo("Last turn reasoning output tokens: 14"));
                Assert.That(lines, Has.Some.EqualTo("Goal status: Active"));
                Assert.That(lines, Has.Some.EqualTo("Goal tokens used: 3万"));
                Assert.That(lines, Has.Some.EqualTo("Goal token budget: 20万"));
                Assert.That(lines, Has.Some.EqualTo("Goal time used: 1h 1m 1s"));
                Assert.That(lines, Has.No.Some.Contains("Lifetime tokens"));
                Assert.That(lines, Has.No.Some.Contains("Usage streak"));
                Assert.That(lines, Has.No.Some.Contains("Context used"));
            });
        }

        [Test]
        public void BuildDetailLines_WithoutConversationUsage_ShowsUnavailableMessage()
        {
            WithCulture("en-US", () =>
            {
                var lines = SessionStatisticsDetailBuilder.BuildDetailLines(new ChatSession
                {
                    Title = "Untokenized",
                    CreatedAt = new DateTime(2026, 7, 5, 10, 0, 0),
                    LastActiveAt = new DateTime(2026, 7, 5, 10, 5, 0)
                });

                Assert.That(lines, Has.Some.EqualTo("Token usage: not available for this conversation yet."));
                Assert.That(lines, Has.Some.EqualTo("Goal: not set."));
            });
        }

        [Test]
        public void BuildDetailLines_WithDefaultSessionSettings_ShowsEffectiveModelAndReasoningEffort()
        {
            WithCulture("en-US", () =>
            {
                var lines = SessionStatisticsDetailBuilder.BuildDetailLines(
                    new ChatSession
                    {
                        Title = "Follow default",
                        ThreadId = "thread-2"
                    },
                    new SessionStatisticsDisplayContext
                    {
                        EffectiveModelId = "gpt-5.4-default",
                        EffectiveReasoningEffort = "high"
                    });

                Assert.That(lines, Has.Some.EqualTo("Model: gpt-5.4-default"));
                Assert.That(lines, Has.Some.EqualTo("Reasoning effort: high"));
            });
        }

        [Test]
        public void BuildDetailLines_WithSessionOverrides_PrefersSessionModelAndReasoningEffort()
        {
            WithCulture("en-US", () =>
            {
                var lines = SessionStatisticsDetailBuilder.BuildDetailLines(
                    new ChatSession
                    {
                        Title = "Custom",
                        ThreadId = "thread-3",
                        ModelId = "gpt-custom",
                        ReasoningEffort = "low"
                    },
                    new SessionStatisticsDisplayContext
                    {
                        EffectiveModelId = "gpt-default",
                        EffectiveReasoningEffort = "high"
                    });

                Assert.That(lines, Has.Some.EqualTo("Model: gpt-custom"));
                Assert.That(lines, Has.Some.EqualTo("Reasoning effort: low"));
            });
        }

        private static void WithCulture(string cultureName, Action action)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
            var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

            try
            {
                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            }
        }
    }
}
