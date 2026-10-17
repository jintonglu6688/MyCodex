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
    public sealed class GitCloneService : IGitCloneService
    {
        public Task<GitCloneResult> CloneAsync(
            GitCloneRequest request,
            IProgress<GitCloneProgress> progress,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return Task.Run(() => Clone(request, progress, cancellationToken), cancellationToken);
        }

        private static GitCloneResult Clone(
            GitCloneRequest request,
            IProgress<GitCloneProgress> progress,
            CancellationToken cancellationToken)
        {
            var repositoryUrl = NormalizeRequired(request.RepositoryUrl, "GitCloneService.Error.RepositoryUrlRequired", "Repository URL cannot be empty.");
            var parentDirectory = NormalizeRequired(request.TargetParentDirectory, "GitCloneService.Error.TargetParentRequired", "Target directory cannot be empty.");
            var folderName = NormalizeRequired(request.TargetFolderName, "GitCloneService.Error.TargetFolderRequired", "Target folder name cannot be empty.");
            ValidateFolderName(folderName);

            if (!Directory.Exists(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            var targetPath = Path.Combine(parentDirectory, folderName);
            if (Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any())
            {
                throw new InvalidOperationException(
                    SharedLocalization.Text("GitCloneService.Error.TargetExists", "The target folder already exists and is not empty."));
            }

            var output = new StringBuilder();
            var error = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = BundledGitRuntime.ResolveExecutable(),
                    Arguments = BuildCloneArguments(repositoryUrl, folderName, request.RecurseSubmodules),
                    WorkingDirectory = parentDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                process.OutputDataReceived += (sender, e) =>
                {
                    AppendLine(output, e.Data);
                    Report(progress, e.Data, false);
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    AppendLine(error, e.Data);
                    Report(progress, e.Data, true);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.WaitForExit(200))
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        continue;
                    }

                    TryKill(process);
                    throw new OperationCanceledException(cancellationToken);
                }

                process.WaitForExit();

                return new GitCloneResult
                {
                    Success = process.ExitCode == 0,
                    ExitCode = process.ExitCode,
                    TargetPath = targetPath,
                    StandardOutput = output.ToString(),
                    StandardError = error.ToString()
                };
            }
        }

        private static string BuildCloneArguments(string repositoryUrl, string folderName, bool recurseSubmodules)
        {
            var arguments = recurseSubmodules
                ? new[] { "clone", "--progress", "--recurse-submodules", repositoryUrl, folderName }
                : new[] { "clone", "--progress", repositoryUrl, folderName };
            return GitCommandLineBuilder.BuildArguments(arguments);
        }

        private static void AppendLine(StringBuilder builder, string text)
        {
            if (builder == null || text == null)
            {
                return;
            }

            builder.AppendLine(GitOutputSanitizer.RedactSensitiveText(text));
        }

        private static void Report(IProgress<GitCloneProgress> progress, string text, bool isError)
        {
            if (progress == null || text == null)
            {
                return;
            }

            progress.Report(new GitCloneProgress
            {
                Text = GitOutputSanitizer.RedactSensitiveText(text),
                IsError = isError
            });
        }

        private static string NormalizeRequired(string value, string resourceKey, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(SharedLocalization.Text(resourceKey, fallback));
            }

            return value.Trim();
        }

        private static void ValidateFolderName(string folderName)
        {
            if (string.Equals(folderName, ".", StringComparison.Ordinal) ||
                string.Equals(folderName, "..", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitCloneService.Error.InvalidTargetFolder", "Target folder name contains invalid characters."));
            }

            if (folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitCloneService.Error.InvalidTargetFolder", "Target folder name contains invalid characters."));
            }
        }

        private static void TryKill(Process process)
        {
            MyCodex.Infrastructure.Processes.ProcessTreeTerminator.TryKill(process);
        }
    }
}
