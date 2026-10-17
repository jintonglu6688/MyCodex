using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.Processes;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Git
{
    public sealed class GitEnvironmentService : IGitEnvironmentService
    {
        public Task<GitEnvironmentSnapshot> ReadStatusAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() => ReadStatus(cancellationToken), cancellationToken);
        }

        public Task SetGlobalUserNameAsync(string value, CancellationToken cancellationToken)
        {
            return SetGlobalConfigAsync("user.name", value, cancellationToken);
        }

        public Task SetGlobalUserEmailAsync(string value, CancellationToken cancellationToken)
        {
            return SetGlobalConfigAsync("user.email", value, cancellationToken);
        }

        private static async Task SetGlobalConfigAsync(string key, string value, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(SharedLocalization.Text("GitEnvironmentService.Error.ValueRequired", "Value cannot be empty."));
            }

            var result = await Task.Run(
                () => RunProcess("git", GitCommandLineBuilder.BuildArguments(new[] { "config", "--global", key, value.Trim() }), 10000, cancellationToken),
                cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(GetBestOutput(result));
            }
        }

        private static GitEnvironmentSnapshot ReadStatus(CancellationToken cancellationToken)
        {
            var details = new StringBuilder();
            var gitVersion = ReadValue(details, "git --version", "git", "--version", 8000, cancellationToken);
            var gitPath = ReadValue(details, "where git", "where", "git", 8000, cancellationToken);
            var userName = ReadValue(details, "git config --global --get user.name", "git", "config --global --get user.name", 8000, cancellationToken);
            var userEmail = ReadValue(details, "git config --global --get user.email", "git", "config --global --get user.email", 8000, cancellationToken);
            var credentialHelper = ReadValue(details, "git config --global --get credential.helper", "git", "config --global --get credential.helper", 8000, cancellationToken);
            var gcm = ReadFirstAvailable(
                details,
                cancellationToken,
                Tuple.Create("git credential-manager --version", "git", "credential-manager --version"),
                Tuple.Create("git-credential-manager --version", "git-credential-manager", "--version"));
            var sshKeys = ReadSshKeys();
            var githubSsh = ReadValue(
                details,
                "ssh -T git@github.com",
                "ssh",
                "-o BatchMode=yes -o ConnectTimeout=5 -T git@github.com",
                8000,
                cancellationToken);

            return new GitEnvironmentSnapshot
            {
                Runtime = SharedLocalization.Text("GitEnvironmentService.Runtime.LocalWindows", "Local Windows"),
                GitVersion = NormalizeMultilineValue(gitVersion),
                GitPath = NormalizeMultilineValue(gitPath),
                GlobalUserName = NormalizeMultilineValue(userName),
                GlobalUserEmail = NormalizeMultilineValue(userEmail),
                CredentialHelper = NormalizeMultilineValue(credentialHelper),
                GitCredentialManager = NormalizeMultilineValue(gcm),
                SshKeys = NormalizeMultilineValue(sshKeys),
                GitHubSsh = NormalizeMultilineValue(githubSsh),
                DetailedOutput = details.ToString()
            };
        }

        private static string ReadFirstAvailable(
            StringBuilder details,
            CancellationToken cancellationToken,
            params Tuple<string, string, string>[] commands)
        {
            foreach (var command in commands)
            {
                var value = ReadValue(details, command.Item1, command.Item2, command.Item3, 8000, cancellationToken);
                if (!IsMissingOrEmpty(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string ReadValue(
            StringBuilder details,
            string title,
            string fileName,
            string arguments,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            var result = RunProcess(fileName, arguments, timeoutMilliseconds, cancellationToken);
            AppendDetails(details, title, result);
            return GetBestOutput(result);
        }

        private static ProcessRunResult RunProcess(string fileName, string arguments, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            try
            {
                // The shared runner drains stdout/stderr while the process runs, so large output cannot block it.
                var result = ProcessExecutionRunner.Run(new ProcessExecutionRequest
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = string.Equals(fileName, "git", StringComparison.OrdinalIgnoreCase)
                            ? BundledGitRuntime.ResolveExecutable() : fileName,
                        Arguments = arguments ?? string.Empty,
                        WorkingDirectory = Environment.CurrentDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    },
                    TimeoutMilliseconds = timeoutMilliseconds,
                    TimeoutMessage = SharedLocalization.Text("GitEnvironmentService.Error.Timeout", "Command timed out."),
                    CancellationToken = cancellationToken
                });
                return new ProcessRunResult
                {
                    ExitCode = result.ExitCode,
                    StandardOutput = result.StandardOutput ?? string.Empty,
                    StandardError = result.StandardError ?? string.Empty
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ProcessRunResult.Failed(ex.Message);
            }
        }

        private static string ReadSshKeys()
        {
            try
            {
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(profile))
                {
                    return string.Empty;
                }

                var sshDirectory = Path.Combine(profile, ".ssh");
                if (!Directory.Exists(sshDirectory))
                {
                    return string.Empty;
                }

                var keys = Directory.GetFiles(sshDirectory, "*.pub")
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return keys.Length == 0 ? string.Empty : string.Join(", ", keys);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static void AppendDetails(StringBuilder details, string title, ProcessRunResult result)
        {
            if (details == null)
            {
                return;
            }

            details.AppendLine("[" + title + "]");
            details.AppendLine("ExitCode: " + result.ExitCode);
            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                details.AppendLine("stdout:");
                details.AppendLine(GitOutputSanitizer.RedactSensitiveText(result.StandardOutput.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                details.AppendLine("stderr:");
                details.AppendLine(GitOutputSanitizer.RedactSensitiveText(result.StandardError.Trim()));
            }

            details.AppendLine();
        }

        private static string NormalizeMultilineValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return GitOutputSanitizer.RedactSensitiveText(value.Trim().Replace("\r\n", " | ").Replace("\n", " | "));
        }

        private static string GetBestOutput(ProcessRunResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return GitOutputSanitizer.RedactSensitiveText(result.StandardOutput.Trim());
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                return GitOutputSanitizer.RedactSensitiveText(result.StandardError.Trim());
            }

            return string.Empty;
        }

        private static bool IsMissingOrEmpty(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return value.IndexOf("not recognized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("is not a git command", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("cannot find", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("No such file", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class ProcessRunResult
        {
            public int ExitCode { get; set; }

            public string StandardOutput { get; set; }

            public string StandardError { get; set; }

            public static ProcessRunResult Failed(string message)
            {
                return new ProcessRunResult
                {
                    ExitCode = -1,
                    StandardError = message ?? string.Empty,
                    StandardOutput = string.Empty
                };
            }
        }
    }
}
