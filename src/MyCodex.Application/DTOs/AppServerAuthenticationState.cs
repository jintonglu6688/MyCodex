namespace MyCodex.Application.DTOs
{
    public class AppServerAuthenticationState
    {
        public bool HasAccount { get; set; }

        public bool IsAuthenticated { get; set; }

        public bool RequiresOpenAiAuth { get; set; }

        public string AuthMode { get; set; }

        public string AccountType { get; set; }

        public string Email { get; set; }

        public string PlanType { get; set; }
    }
}
