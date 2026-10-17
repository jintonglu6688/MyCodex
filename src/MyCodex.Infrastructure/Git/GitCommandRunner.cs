using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.Processes;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Git
{
    public sealed class GitCommandRunner : IGitCommandRunner
    {
        private readonly int _timeoutMilliseconds;

        public GitCommandRunner()
            : this(15000)
        {
        }

        public GitCommandRunner(int timeoutMilliseconds)
        {
            _timeoutMilliseconds = timeoutMilliseconds <= 0 ? 15000 : timeoutMilliseconds;
        }

        public GitCommandResult Run(string workingDirectory, string arguments)
        {
            return Run(workingDirectory, arguments, _timeoutMilliseconds);
        }

        public GitCommandResult Run(string workingDirectory, string arguments, int timeoutMilliseconds)
        {
            return Run(workingDirectory, arguments, timeoutMilliseconds, CancellationToken.None);
        }

        public GitCommandResult Run(
            string workingDirectory,
            string arguments,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitCommandRunner.Error.WorkingDirectoryRequired", "Working directory cannot be empty."),
                    nameof(workingDirectory));
            }

            if (!Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException(
                    SharedLocalization.Text("GitCommandRunner.Error.WorkingDirectoryNotFound", "The working directory does not exist."));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = BundledGitRuntime.ResolveExecutable(),
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = workingDirectory,
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
                TimeoutMilliseconds = NormalizeTimeout(timeoutMilliseconds),
                TimeoutMessage = SharedLocalization.Text("GitCommandRunner.Error.Timeout", "Git command timed out."),
                CancellationToken = cancellationToken,
                OutputTransform = GitOutputSanitizer.RedactSensitiveText
            });
            return new GitCommandResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError
            };
        }

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments, int timeoutMilliseconds)
        {
            return Run(workingDirectory, GitCommandLineBuilder.BuildArguments(arguments), timeoutMilliseconds);
        }

        public GitCommandResult Run(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Run(workingDirectory, GitCommandLineBuilder.BuildArguments(arguments), timeoutMilliseconds, cancellationToken);
        }

        public GitCommandResult RunSuccessful(string workingDirectory, string arguments)
        {
            var result = Run(workingDirectory, arguments);
            if (!result.Success)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                    ? SharedLocalization.Text("GitCommandRunner.Error.CommandFailed", "Git command failed.")
                    : result.StandardError.Trim());
            }

            return result;
        }

        public GitCommandResult RunSuccessful(string workingDirectory, IReadOnlyList<string> arguments)
        {
            var result = Run(workingDirectory, arguments, _timeoutMilliseconds);
            if (!result.Success)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                    ? SharedLocalization.Text("GitCommandRunner.Error.CommandFailed", "Git command failed.")
                    : result.StandardError.Trim());
            }

            return result;
        }

        private int NormalizeTimeout(int timeoutMilliseconds)
        {
            return timeoutMilliseconds <= 0 ? _timeoutMilliseconds : timeoutMilliseconds;
        }

    }
}
