using System;

namespace MyCodex.Application.DTOs
{
    public class AppServerRateLimitWindowInfo
    {
        public decimal UsedPercent { get; set; }

        public int? WindowDurationMinutes { get; set; }

        public DateTime? ResetsAtLocalTime { get; set; }
    }
}
