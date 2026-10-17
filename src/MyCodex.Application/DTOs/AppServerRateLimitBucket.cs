namespace MyCodex.Application.DTOs
{
    public class AppServerRateLimitBucket
    {
        public string LimitId { get; set; }

        public string LimitName { get; set; }

        public string PlanType { get; set; }

        public AppServerRateLimitWindowInfo Primary { get; set; }

        public AppServerRateLimitWindowInfo Secondary { get; set; }

        public AppServerCreditsInfo Credits { get; set; }
    }
}
