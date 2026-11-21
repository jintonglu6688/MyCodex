using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;

namespace MyCodex.Infrastructure.AppServer
{
    // Probe output (credential.helper, remote URLs) can carry tokens and reaches details and the clipboard,
    // so every value leaving here is redacted.
    internal static class UnixGitEnvironmentText
    {
        public static void AppendDetails(StringBuilder details, string title, int exitCode, string standardOutput, string standardError)
        {
            standardOutput = GitOutputSanitizer.RedactSensitiveText(standardOutput);
            standardError = GitOutputSanitizer.RedactSensitiveText(standardError);
            details.AppendLine(">> " + title);
            details.AppendLine("ExitCode: " + exitCode);
            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                details.AppendLine(standardOutput.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(standardError))
            {
                details.AppendLine(standardError.TrimEnd());
            }

            details.AppendLine();
        }

        public static string GetBestOutput(string standardOutput, string standardError)
        {
            return GitOutputSanitizer.RedactSensitiveText(string.IsNullOrWhiteSpace(standardOutput)
                ? standardError
                : standardOutput);
        }

        public static GitEnvironmentSnapshot BuildSnapshot(
            string runtime,
            string gitVersion,
            string gitPath,
            string userName,
            string userEmail,
            string credentialHelper,
            string gitCredentialManager,
            string sshKeys,
            string gitHubSsh,
            StringBuilder details)
        {
            return new GitEnvironmentSnapshot
            {
                Runtime = runtime,
                GitVersion = NormalizeMultilineValue(gitVersion),
                GitPath = NormalizeMultilineValue(gitPath),
                GlobalUserName = NormalizeMultilineValue(userName),
                GlobalUserEmail = NormalizeMultilineValue(userEmail),
                CredentialHelper = NormalizeMultilineValue(credentialHelper),
                GitCredentialManager = NormalizeMultilineValue(gitCredentialManager),
                SshKeys = NormalizeMultilineValue(sshKeys),
                GitHubSsh = NormalizeMultilineValue(gitHubSsh),
                DetailedOutput = details.ToString()
            };
        }

        private static string NormalizeMultilineValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();
        }
    }
}
