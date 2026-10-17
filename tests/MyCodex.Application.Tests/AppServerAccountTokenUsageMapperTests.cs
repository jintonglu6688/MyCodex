using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerAccountTokenUsageMapperTests
    {
        [Test]
        public void BuildSnapshot_MapsSummaryAndDailyBuckets()
        {
            var result = AppServerAccountTokenUsageMapper.BuildSnapshot(new Dictionary<string, object>
            {
                {
                    "summary",
                    new Dictionary<string, object>
                    {
                        { "lifetimeTokens", "123456789012" },
                        { "peakDailyTokens", 42000L },
                        { "longestRunningTurnSec", 3600L },
                        { "currentStreakDays", 3L },
                        { "longestStreakDays", 14L }
                    }
                },
                {
                    "dailyUsageBuckets",
                    new object[]
                    {
                        new Dictionary<string, object> { { "startDate", "2026-06-17" }, { "tokens", "1200" } },
                        new Dictionary<string, object> { { "startDate", "2026-06-18" }, { "tokens", 3400L } }
                    }
                }
            });

            Assert.That(result.LifetimeTokens, Is.EqualTo(123456789012L));
            Assert.That(result.PeakDailyTokens, Is.EqualTo(42000L));
            Assert.That(result.LongestRunningTurnSeconds, Is.EqualTo(3600L));
            Assert.That(result.CurrentStreakDays, Is.EqualTo(3L));
            Assert.That(result.LongestStreakDays, Is.EqualTo(14L));
            Assert.That(result.DailyBuckets, Has.Count.EqualTo(2));
            Assert.That(result.DailyBuckets[1].StartDate, Is.EqualTo("2026-06-18"));
            Assert.That(result.DailyBuckets[1].Tokens, Is.EqualTo(3400L));
        }

        [Test]
        public void BuildSnapshot_MissingDailyBucketsReturnsEmptyList()
        {
            var result = AppServerAccountTokenUsageMapper.BuildSnapshot(new Dictionary<string, object>
            {
                {
                    "summary",
                    new Dictionary<string, object>
                    {
                        { "lifetimeTokens", 12L }
                    }
                }
            });

            Assert.That(result.LifetimeTokens, Is.EqualTo(12L));
            Assert.That(result.DailyBuckets, Is.Empty);
        }
    }
}
