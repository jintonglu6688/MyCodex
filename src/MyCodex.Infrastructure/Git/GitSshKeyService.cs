using System;
using System.Collections.Generic;
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
    public sealed class GitSshKeyService : IGitSshKeyService
    {
        public Task<GitSshKeySnapshot> ReadKeysAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() => ReadKeys(cancellationToken), cancellationToken);
        }

        public Task<GitSshTestResult> TestGitHubSshAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() => TestGitHubSsh(cancellationToken), cancellationToken);
        }

        private static GitSshKeySnapshot ReadKeys(CancellationToken cancellationToken)
        {
            var details = new StringBuilder();
            var sshDirectory = GetSshDirectory();
            var keys = new List<GitSshKeyEntry>();
            details.AppendLine(string.Format(
                SharedLocalization.Text("GitSshKeyService.Details.Directory.Template", "SSH directory: {0}"),
                sshDirectory));

            if (string.IsNullOrWhiteSpace(sshDirectory) || !Directory.Exists(sshDirectory))
            {
                details.AppendLine(SharedLocalization.Text("GitSshKeyService.Details.DirectoryMissing", "SSH directory was not found."));
                return new GitSshKeySnapshot
                {
                    SshDirectory = sshDirectory,
                    Keys = keys,
                    Details = details.ToString()
                };
            }

            foreach (var publicKeyPath in Directory.GetFiles(sshDirectory, "*.pub").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                keys.Add(ReadKey(publicKeyPath, details, cancellationToken));
            }

            if (keys.Count == 0)
            {
                details.AppendLine(SharedLocalization.Text("GitSshKeyService.Details.NoKeys", "No public SSH keys were found."));
            }

            return new GitSshKeySnapshot
            {
                SshDirectory = sshDirectory,
                Keys = keys,
                Details = details.ToString()
            };
        }

        private static GitSshKeyEntry ReadKey(string publicKeyPath, StringBuilder details, CancellationToken cancellationToken)
        {
            var name = Path.GetFileName(publicKeyPath);
            var privateKeyPath = publicKeyPath.EndsWith(".pub", StringComparison.OrdinalIgnoreCase)
                ? publicKeyPath.Substring(0, publicKeyPath.Length - 4)
                : publicKeyPath;
            var publicKeyText = string.Empty;
            var keyType = string.Empty;
            var comment = string.Empty;
            var fingerprint = string.Empty;

            try
            {
                publicKeyText = File.ReadAllText(publicKeyPath, Encoding.UTF8).Trim();
                ParsePublicKey(publicKeyText, out keyType, out comment);
            }
            catch (Exception ex)
            {
                details.AppendLine(string.Format(
                    SharedLocalization.Text("GitSshKeyService.Details.ReadFailed.Template", "{0}: failed to read public key: {1}"),
                    name,
                    ex.Message));
            }

            var fingerprintResult = RunProcess(
                "ssh-keygen",
                GitCommandLineBuilder.BuildArguments(new[] { "-lf", publicKeyPath }),
                8000,
                cancellationToken);
            fingerprint = GetBestOutput(fingerprintResult);
            if (fingerprintResult.ExitCode != 0 && !string.IsNullOrWhiteSpace(fingerprint))
            {
                details.AppendLine(string.Format(
                    SharedLocalization.Text("GitSshKeyService.Details.FingerprintFailed.Template", "{0}: failed to read fingerprint: {1}"),
                    name,
                    fingerprint));
            }

            details.AppendLine(string.Format(
                SharedLocalization.Text("GitSshKeyService.Details.Key.Template", "{0}: {1}; {2}; private: {3}"),
                name,
                string.IsNullOrWhiteSpace(keyType) ? SharedLocalization.Text("GitSshKeyService.Value.Unknown", "Unknown") : keyType,
                string.IsNullOrWhiteSpace(fingerprint) ? SharedLocalization.Text("GitSshKeyService.Value.NoFingerprint", "No fingerprint") : fingerprint,
                File.Exists(privateKeyPath)
                    ? SharedLocalization.Text("GitSshKeyService.Value.Yes", "Yes")
                    : SharedLocalization.Text("GitSshKeyService.Value.No", "No")));

            return new GitSshKeyEntry
            {
                Name = name,
                PublicKeyPath = publicKeyPath,
                PrivateKeyPath = privateKeyPath,
                HasPrivateKey = File.Exists(privateKeyPath),
                KeyType = keyType,
                Fingerprint = fingerprint,
                Comment = comment,
                PublicKeyText = publicKeyText
            };
        }

        private static GitSshTestResult TestGitHubSsh(CancellationToken cancellationToken)
        {
            var result = RunProcess(
                "ssh",
                "-o BatchMode=yes -o ConnectTimeout=8 -o StrictHostKeyChecking=accept-new -T git@github.com",
                12000,
                cancellationToken);
            var output = GetBestOutput(result);
            var succeeded = output.IndexOf("successfully authenticated", StringComparison.OrdinalIgnoreCase) >= 0;
            var details = new StringBuilder();
            AppendDetails(details, "ssh -T git@github.com", result);

            return new GitSshTestResult
            {
                Succeeded = succeeded,
                ExitCode = result.ExitCode,
                Output = output,
                Details = details.ToString()
            };
        }

        private static string GetSshDirectory()
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrWhiteSpace(profile) ? string.Empty : Path.Combine(profile, ".ssh");
        }

        private static void ParsePublicKey(string publicKeyText, out string keyType, out string comment)
        {
            keyType = string.Empty;
            comment = string.Empty;
            if (string.IsNullOrWhiteSpace(publicKeyText))
            {
                return;
            }

            var parts = publicKeyText.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                keyType = parts[0];
            }

            if (parts.Length > 2)
            {
                comment = parts[2];
            }
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
                        FileName = fileName,
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
                    TimeoutMessage = SharedLocalization.Text("GitSshKeyService.Error.Timeout", "Command timed out."),
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

        private static void AppendDetails(StringBuilder details, string title, ProcessRunResult result)
        {
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
