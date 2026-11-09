using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    // Usage (quota) query settings of a saved provider. Secrets are reported only as present.
    public sealed class AuthenticationUsageSettings
    {
        public string ProviderId { get; set; }
        public string ProviderName { get; set; }
        // Template the backend recognizes from the provider address ("token_plan", "balance") or null.
        public string Detected { get; set; }
        public bool Configured { get; set; }
        public bool Enabled { get; set; }
        public string TemplateType { get; set; }
        public string BaseUrl { get; set; }
        public bool HasApiKey { get; set; }
        public bool HasAccessToken { get; set; }
        public string UserId { get; set; }
        public int Timeout { get; set; } = 10;
        // Minutes between automatic refreshes while User Center is open; 0 turns them off.
        public int AutoQueryInterval { get; set; } = 5;
        public string Code { get; set; }
    }

    public sealed class AuthenticationUsageEdit
    {
        public string ProviderId { get; set; }
        public bool Enabled { get; set; }
        public string TemplateType { get; set; }
        public string BaseUrl { get; set; }
        // null keeps the saved value; Clear* removes it.
        public string ApiKey { get; set; }
        public bool ClearApiKey { get; set; }
        public string AccessToken { get; set; }
        public bool ClearAccessToken { get; set; }
        public string UserId { get; set; }
        public int Timeout { get; set; } = 10;
        public int AutoQueryInterval { get; set; } = 5;
        public string Code { get; set; }
    }

    public sealed class AuthenticationUsageResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public List<AuthenticationUsageItem> Items { get; set; } = new List<AuthenticationUsageItem>();
    }

    public sealed class AuthenticationUsageItem
    {
        public string PlanName { get; set; }
        public decimal? Remaining { get; set; }
        public decimal? Used { get; set; }
        public decimal? Total { get; set; }
        public string Unit { get; set; }
        public bool? IsValid { get; set; }
        public string InvalidMessage { get; set; }
        // Reset time or other provider detail, as the backend reports it.
        public string Extra { get; set; }
    }
}
