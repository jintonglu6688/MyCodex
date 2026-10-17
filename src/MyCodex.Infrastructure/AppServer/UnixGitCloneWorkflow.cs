using System;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class UnixGitCloneCommandResult
    {
        public int ExitCode { get; set; }

        public string StandardOutput { get; set; }

        public string StandardError { get; set; }
    }

    internal static class UnixGitCloneWorkflow
    {
        internal static void ValidateRequest(
            GitCloneRequest request,
            out string repositoryUrl,
            out string parentDirectory,
            out string folderName)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            repositoryUrl = UnixGitCloneScript.NormalizeRequired(request.RepositoryUrl, "repositoryUrl");
            parentDirectory = UnixGitCloneScript.NormalizeRequired(request.TargetParentDirectory, "targetParentDirectory");
            folderName = UnixGitCloneScript.NormalizeRequired(request.TargetFolderName, "targetFolderName");
            UnixGitCloneScript.ValidateFolderName(folderName);
        }

        internal static GitCloneResult BuildResult(
            GitCloneRequest request,
            string parentDirectory,
            string folderName,
            UnixGitCloneCommandResult result,
            IProgress<GitCloneProgress> progress)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            string targetPath;
            var standardOutput = UnixGitCloneScript.ExtractCloneTargetPath(result.StandardOutput, out targetPath);
            UnixGitCloneScript.ReportBufferedProgress(progress, standardOutput, false);
            UnixGitCloneScript.ReportBufferedProgress(progress, result.StandardError, true);

            return new GitCloneResult
            {
                Success = result.ExitCode == 0,
                ExitCode = result.ExitCode,
                TargetPath = string.IsNullOrWhiteSpace(targetPath) ? UnixGitCloneScript.CombineUnixPath(parentDirectory, folderName) : targetPath,
                StandardOutput = GitOutputSanitizer.RedactSensitiveText(standardOutput ?? string.Empty),
                StandardError = GitOutputSanitizer.RedactSensitiveText(result.StandardError ?? string.Empty)
            };
        }
    }
}
