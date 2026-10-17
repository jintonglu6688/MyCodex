namespace MyCodex.Application.DTOs
{
    public sealed class GitHubCliAccountSnapshot
    {
        public bool IsInstalled { get; set; }

        public bool IsAuthenticated { get; set; }

        public string Version { get; set; }

        public string Login { get; set; }

        public string DisplayName { get; set; }

        public string Email { get; set; }

        public string ProfileUrl { get; set; }

        public string GitProtocol { get; set; }

        public string TokenScopes { get; set; }

        public string Status { get; set; }

        public string Details { get; set; }
    }
}
