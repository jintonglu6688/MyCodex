using System;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerRateLimitResetCredit
    {
        public string Id { get; set; }

        public string ResetType { get; set; }

        public string Status { get; set; }

        public DateTime GrantedAtLocalTime { get; set; }

        public DateTime? ExpiresAtLocalTime { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }
    }
}
