using System.Text;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class UnixGitEnvironmentText
    {
        public static void AppendDetails(StringBuilder details, string title, int exitCode, string standardOutput, string standardError)
        {
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
            return string.IsNullOrWhiteSpace(standardOutput)
                ? standardError ?? string.Empty
                : standardOutput;
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
