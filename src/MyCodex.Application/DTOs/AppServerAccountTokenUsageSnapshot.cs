using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerAccountTokenUsageSnapshot
    {
        public AppServerAccountTokenUsageSnapshot()
        {
            DailyBuckets = new List<AppServerAccountTokenUsageDailyBucket>();
        }

        public long? LifetimeTokens { get; set; }

        public long? PeakDailyTokens { get; set; }

        public long? LongestRunningTurnSeconds { get; set; }

        public long? CurrentStreakDays { get; set; }

        public long? LongestStreakDays { get; set; }

        public List<AppServerAccountTokenUsageDailyBucket> DailyBuckets { get; private set; }
    }

    public sealed class AppServerAccountTokenUsageDailyBucket
    {
        public string StartDate { get; set; }

        public long Tokens { get; set; }
    }
}
