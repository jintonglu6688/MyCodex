using System;
using System.Diagnostics;
using System.Text;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Git
{
    public sealed class GitDiffCommandRunner : IGitDiffCommandRunner
    {
        public GitCommandResult Run(string workspacePath, string arguments, int timeoutMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitDiffService.Error.WorkspacePathRequired", "Workspace path cannot be empty."),
                    nameof(workspacePath));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = BundledGitRuntime.ResolveExecutable(),
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = workspacePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var result = MyCodex.Infrastructure.Processes.ProcessExecutionRunner.Run(new MyCodex.Infrastructure.Processes.ProcessExecutionRequest
            {
                StartInfo = startInfo,
                TimeoutMilliseconds = timeoutMilliseconds,
                TimeoutMessage = SharedLocalization.Text("GitDiffService.Error.Timeout", "Git command timed out."),
                OutputTransform = GitOutputSanitizer.RedactSensitiveText
            });

            if (result.ExitCode != 0)
            {
                var errorText = string.IsNullOrWhiteSpace(result.StandardError)
                    ? SharedLocalization.Text("GitDiffService.Error.GitCommandFailed", "Git command failed.")
                    : result.StandardError.Trim();
                throw new InvalidOperationException(errorText);
            }

            return new GitCommandResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput ?? string.Empty,
                StandardError = result.StandardError ?? string.Empty
            };
        }
    }
}
