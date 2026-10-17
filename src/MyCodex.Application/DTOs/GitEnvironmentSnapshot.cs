namespace MyCodex.Application.DTOs
{
    public sealed class GitEnvironmentSnapshot
    {
        public string Runtime { get; set; }

        public string GitVersion { get; set; }

        public string GitPath { get; set; }

        public string GlobalUserName { get; set; }

        public string GlobalUserEmail { get; set; }

        public string CredentialHelper { get; set; }

        public string GitCredentialManager { get; set; }

        public string SshKeys { get; set; }

        public string GitHubSsh { get; set; }

        public string DetailedOutput { get; set; }
    }
}
