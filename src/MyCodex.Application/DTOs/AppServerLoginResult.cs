namespace MyCodex.Application.DTOs
{
    public class AppServerLoginResult
    {
        public string LoginType { get; set; }

        public string LoginId { get; set; }

        public string AuthUrl { get; set; }

        public string VerificationUrl { get; set; }

        public string UserCode { get; set; }
    }
}
