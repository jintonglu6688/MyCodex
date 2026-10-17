using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class AppServerRateLimitsSnapshot
    {
        public AppServerRateLimitsSnapshot()
        {
            Buckets = new List<AppServerRateLimitBucket>();
        }

        public List<AppServerRateLimitBucket> Buckets { get; }

        public int? RateLimitResetCreditsAvailableCount { get; set; }

        public List<AppServerRateLimitResetCredit> ResetCredits { get; set; }
    }
}
