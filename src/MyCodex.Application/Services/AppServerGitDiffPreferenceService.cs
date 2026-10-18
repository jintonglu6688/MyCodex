using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed class AppServerGitDiffPreferenceService : IGitDiffReader
    {
        private readonly IAppServerGitDiffClient _appServerClient;
        private readonly IGitDiffReader _fallback;
        private readonly string _runtimeSource;

        public AppServerGitDiffPreferenceService(IAppServerGitDiffClient appServerClient, IGitDiffReader fallback)
            : this(appServerClient, fallback, null)
        {
        }

        public AppServerGitDiffPreferenceService(IAppServerGitDiffClient appServerClient, IGitDiffReader fallback, string runtimeSource)
        {
            _appServerClient = appServerClient ?? throw new ArgumentNullException(nameof(appServerClient));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _runtimeSource = runtimeSource;
        }

        public Task<GitOperationResult> ReadDiffAsync(string workspacePath, GitChangedFileEntry entry, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(_runtimeSource))
            {
                return _fallback.ReadDiffAsync(workspacePath, entry, cancellationToken);
            }

            return ReadDiffAsync(workspacePath, entry, _runtimeSource, cancellationToken);
        }

        public Task<GitOperationResult> ReadDiffAsync(string workspacePath, GitChangedFileEntry entry, GitDiffKind diffKind, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(_runtimeSource))
            {
                return _fallback.ReadDiffAsync(workspacePath, entry, diffKind, cancellationToken);
            }

            return ReadDiffAsync(workspacePath, entry, diffKind, _runtimeSource, cancellationToken);
        }

        public Task<GitOperationResult> ReadConflictDetailsAsync(string workspacePath, GitChangedFileEntry entry, CancellationToken cancellationToken = default(CancellationToken))
        {
            return _fallback.ReadConflictDetailsAsync(workspacePath, entry, cancellationToken);
        }

        public Task<GitOperationResult> ReadDiffAsync(
            string workspacePath,
            GitChangedFileEntry entry,
            string runtimeSource,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ReadDiffAsync(workspacePath, entry, GitDiffKind.Auto, runtimeSource, cancellationToken);
        }

        public async Task<GitOperationResult> ReadDiffAsync(
            string workspacePath,
            GitChangedFileEntry entry,
            GitDiffKind diffKind,
            string runtimeSource,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!ShouldUseAppServer(runtimeSource) || diffKind != GitDiffKind.Auto)
            {
                return await _fallback.ReadDiffAsync(workspacePath, entry, diffKind, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var result = await _appServerClient.GetGitDiffToRemoteAsync(workspacePath, cancellationToken).ConfigureAwait(false);
                if (result != null)
                {
                    var diff = result.Diff ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(entry?.FilePath) &&
                        HasDiffSections(diff) &&
                        !TryExtractFileDiff(diff, entry.FilePath, out diff))
                    {
                        return await _fallback.ReadDiffAsync(workspacePath, entry, diffKind, cancellationToken).ConfigureAwait(false);
                    }

                    return new GitOperationResult
                    {
                        Success = true,
                        ExitCode = 0,
                        StandardOutput = diff,
                        DisplayMessage = SharedLocalization.Text(
                            "AppServerGitDiffPreferenceService.Operation.AppServerDiffLoaded",
                            "Loaded Git diff from App Server.")
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[GitDiff] App Server diff failed; using local Git. ExceptionType={0}", ex.GetType().Name);
            }

            return await _fallback.ReadDiffAsync(workspacePath, entry, diffKind, cancellationToken).ConfigureAwait(false);
        }

        private static bool ShouldUseAppServer(string runtimeSource)
        {
            return string.Equals(runtimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase) ||
                   CodexRuntimeSources.IsRemoteUnixSsh(runtimeSource);
        }

        private static bool HasDiffSections(string diff)
        {
            if (string.IsNullOrEmpty(diff))
            {
                return false;
            }

            return diff.StartsWith("diff --git ", StringComparison.Ordinal) ||
                   diff.IndexOf("\ndiff --git ", StringComparison.Ordinal) >= 0 ||
                   diff.IndexOf("\rdiff --git ", StringComparison.Ordinal) >= 0;
        }

        private static bool TryExtractFileDiff(string diff, string filePath, out string fileDiff)
        {
            fileDiff = null;
            if (string.IsNullOrEmpty(diff) || string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            var normalizedPath = NormalizeDiffPath(filePath);
            var lines = diff.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sectionStart = -1;
            var sectionEnd = -1;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    continue;
                }

                if (sectionStart >= 0)
                {
                    sectionEnd = i;
                    break;
                }

                if (HeaderMatchesPath(line, normalizedPath))
                {
                    sectionStart = i;
                }
            }

            if (sectionStart < 0)
            {
                return false;
            }

            if (sectionEnd < 0)
            {
                sectionEnd = lines.Length;
            }

            fileDiff = string.Join(Environment.NewLine, lines, sectionStart, sectionEnd - sectionStart);
            return true;
        }

        private static bool HeaderMatchesPath(string header, string normalizedPath)
        {
            var marker = "diff --git ";
            if (!header.StartsWith(marker, StringComparison.Ordinal))
            {
                return false;
            }

            var parts = header.Substring(marker.Length).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var candidate = NormalizeDiffPath(parts[i].Trim('"'));
                if (candidate.StartsWith("a/", StringComparison.Ordinal) ||
                    candidate.StartsWith("b/", StringComparison.Ordinal))
                {
                    candidate = candidate.Substring(2);
                }

                if (string.Equals(candidate, normalizedPath, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeDiffPath(string path)
        {
            return (path ?? string.Empty)
                .Trim()
                .Trim('"')
                .Replace('\\', '/')
                .TrimStart('/');
        }
    }
}
