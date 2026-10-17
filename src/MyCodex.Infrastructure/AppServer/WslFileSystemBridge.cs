using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class WslFileSystemBridge : IWslFileSystemBridge
    {
        private const int DirectoryExistsTimeoutMilliseconds = 5000;
        private const int ListDirectoryTimeoutMilliseconds = 12000;
        private const int DeleteTimeoutMilliseconds = 10000;
        private const int GitInfoTimeoutMilliseconds = 15000;
        private const int GitDiffTimeoutMilliseconds = 20000;
        private const int UnsupportedSessionPathExitCode = 73;

        private const string ListDirectoryScript =
            "set -u\n" +
            "dir=${1:-}\n" +
            "if [ -z \"$dir\" ]; then exit 64; fi\n" +
            "case \"$dir\" in\n" +
            "  \"~\") dir=$HOME ;;\n" +
            "  \"~/\"*) dir=\"$HOME/${dir#~/}\" ;;\n" +
            "esac\n" +
            "if [ ! -d \"$dir\" ]; then exit 66; fi\n" +
            "enc() { printf '%s' \"$1\" | base64 | tr -d '\\n'; }\n" +
            "emit_entry() {\n" +
            "  p=$1\n" +
            "  if [ ! -e \"$p\" ] && [ ! -L \"$p\" ]; then return; fi\n" +
            "  name=${p##*/}\n" +
            "  kind=file\n" +
            "  if [ -d \"$p\" ]; then kind=dir; fi\n" +
            "  hidden=0\n" +
            "  case \"$name\" in .*) hidden=1 ;; esac\n" +
            "  size=$(stat -c %s \"$p\" 2>/dev/null || stat -f %z \"$p\" 2>/dev/null || printf '')\n" +
            "  mtime=$(stat -c %Y \"$p\" 2>/dev/null || stat -f %m \"$p\" 2>/dev/null || printf '')\n" +
            "  printf '%s\\t%s\\t%s\\t%s\\t%s\\t%s\\n' \"$kind\" \"$(enc \"$name\")\" \"$(enc \"$p\")\" \"$hidden\" \"$size\" \"$mtime\"\n" +
            "}\n" +
            "for p in \"$dir\"/* \"$dir\"/.[!.]* \"$dir\"/..?*; do emit_entry \"$p\"; done";

        private const string DeleteCodexSessionScript =
            "set -eu\n" +
            "target=${1:-}\n" +
            "if [ -z \"$target\" ]; then exit 64; fi\n" +
            "target_real=$(realpath -m -- \"$target\")\n" +
            "codex_home=${CODEX_HOME:-$HOME/.codex}\n" +
            "home_real=$(realpath -m -- \"$codex_home\")\n" +
            "sessions_real=$(realpath -m -- \"$home_real/sessions\")\n" +
            "archived_real=$(realpath -m -- \"$home_real/archived_sessions\")\n" +
            "case \"$target_real\" in\n" +
            "  \"$sessions_real\"/*|\"$archived_real\"/*) ;;\n" +
            "  *) echo \"Path is outside supported Codex session directories.\" >&2; exit 73 ;;\n" +
            "esac\n" +
            "if [ -e \"$target_real\" ] || [ -L \"$target_real\" ]; then rm -rf -- \"$target_real\"; fi";

        private readonly IWslCommandExecutor _executor;

        public WslFileSystemBridge()
            : this(new WslCommandExecutor())
        {
        }

        internal WslFileSystemBridge(IWslCommandExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public async Task<string> ResolveHomeDirectoryAsync(WslAppServerConfigSnapshot config)
        {
            var result = await _executor.ExecuteAsync(
                config,
                UnixFileSystemBridgeScripts.ResolveHomeDirectoryScript,
                "mycodex-resolve-wsl-home",
                DirectoryExistsTimeoutMilliseconds);
            if (result.ExitCode != 0)
            {
                throw BuildWslCommandException(result);
            }

            var homePath = (result.StandardOutput ?? string.Empty).Trim();
            if (homePath.IndexOf('\r') >= 0 ||
                homePath.IndexOf('\n') >= 0 ||
                !WorkspacePathUtilities.LooksLikeUnixAbsolutePath(homePath))
            {
                throw new InvalidOperationException("WSL home directory was not returned as an absolute path.");
            }

            return WorkspacePathUtilities.NormalizeUnixAbsolutePath(homePath);
        }

        public async Task<bool> DirectoryExistsAsync(WslAppServerConfigSnapshot config, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var result = await _executor.ExecuteAsync(
                config,
                UnixFileSystemBridgeScripts.DirectoryExistsScript,
                "mycodex-test-directory",
                DirectoryExistsTimeoutMilliseconds,
                path.Trim());
            if (result.ExitCode == 0)
            {
                return true;
            }

            if (result.ExitCode == 1)
            {
                return false;
            }

            throw BuildWslCommandException(result);
        }

        public async Task<bool> FileExistsAsync(WslAppServerConfigSnapshot config, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var result = await _executor.ExecuteAsync(
                config,
                UnixFileSystemBridgeScripts.FileExistsScript,
                "mycodex-test-file",
                DirectoryExistsTimeoutMilliseconds,
                path.Trim());
            if (result.ExitCode == 0)
            {
                return true;
            }

            if (result.ExitCode == 1)
            {
                return false;
            }

            throw BuildWslCommandException(result);
        }

        public async Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(WslAppServerConfigSnapshot config, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("WSL directory path cannot be empty.", nameof(path));
            }

            var result = await _executor.ExecuteAsync(
                config,
                ListDirectoryScript,
                "mycodex-list-directory",
                ListDirectoryTimeoutMilliseconds,
                path.Trim());
            if (result.ExitCode == 0)
            {
                return UnixFileSystemBridgeParsers.ParseDirectoryListing(result.StandardOutput);
            }

            if (result.ExitCode == 66)
            {
                throw new InvalidOperationException("WSL directory was not found.");
            }

            throw BuildWslCommandException(result);
        }

        public async Task DeleteCodexSessionPathAsync(WslAppServerConfigSnapshot config, string sessionPath)
        {
            if (string.IsNullOrWhiteSpace(sessionPath))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("AppServerClient.Error.ThreadFilePathRequired", "Thread file path cannot be empty."),
                    nameof(sessionPath));
            }

            var result = await _executor.ExecuteAsync(
                config,
                DeleteCodexSessionScript,
                "mycodex-delete-codex-session",
                DeleteTimeoutMilliseconds,
                sessionPath.Trim());
            if (result.ExitCode == 0)
            {
                return;
            }

            if (result.ExitCode == UnsupportedSessionPathExitCode)
            {
                throw new InvalidOperationException(
                    SharedLocalization.Text(
                        "AppServerClient.Error.ThreadFilePathUnsupported",
                        "Current thread file path is outside supported Codex state directories and cannot be fully removed."));
            }

            throw BuildWslCommandException(result);
        }

        public async Task<WorkspaceGitInfoSnapshot> ReadGitInfoAsync(WslAppServerConfigSnapshot config, string workspacePath)
        {
            var snapshot = new WorkspaceGitInfoSnapshot();
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return snapshot;
            }

            var result = await _executor.ExecuteAsync(
                config,
                UnixFileSystemBridgeScripts.GitInfoScript,
                "mycodex-read-git-info",
                GitInfoTimeoutMilliseconds,
                workspacePath.Trim());
            if (result.ExitCode != 0)
            {
                return snapshot;
            }

            var values = UnixFileSystemBridgeParsers.ParseKeyValueOutput(result.StandardOutput);
            snapshot.IsGitRepository = true;
            snapshot.Branch = UnixFileSystemBridgeParsers.GetValue(values, "branch");
            snapshot.Sha = UnixFileSystemBridgeParsers.GetValue(values, "sha");
            snapshot.OriginUrl = UnixFileSystemBridgeParsers.GetValue(values, "origin");
            return snapshot;
        }

        public async Task<GitDiffSnapshot> ReadUncommittedChangesAsync(WslAppServerConfigSnapshot config, string workspacePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            var snapshot = new GitDiffSnapshot
            {
                WorkspacePath = workspacePath
            };
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return snapshot;
            }

            var normalizedWorkspacePath = workspacePath.Trim();
            var statusResult = await _executor.ExecuteAsync(
                config,
                UnixFileSystemBridgeScripts.GitStatusScript,
                "mycodex-read-git-status",
                GitDiffTimeoutMilliseconds,
                cancellationToken,
                normalizedWorkspacePath);
            if (statusResult.ExitCode != 0)
            {
                return snapshot;
            }

            snapshot.IsGitRepository = true;
            foreach (var statusEntry in UnixFileSystemBridgeParsers.SplitNullSeparated(statusResult.StandardOutput))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = UnixFileSystemBridgeParsers.ParseStatusEntry(statusEntry);
                if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath))
                {
                    continue;
                }

                entry.DiffText = await BuildDiffTextAsync(config, normalizedWorkspacePath, entry, cancellationToken);
                UnixFileSystemBridgeParsers.PopulateDiffLineCounts(entry);
                snapshot.Files.Add(entry);
            }

            return snapshot;
        }

        private async Task<string> BuildDiffTextAsync(WslAppServerConfigSnapshot config, string workspacePath, GitDiffFileEntry entry, CancellationToken cancellationToken)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath))
            {
                return string.Empty;
            }

            if (string.Equals(entry.ChangeKind, "untracked", StringComparison.OrdinalIgnoreCase))
            {
                return UnixFileSystemBridgeParsers.BuildDiffText(entry, string.Empty, string.Empty);
            }

            var stagedDiff = await ExecuteDiffCommandAsync(config, UnixFileSystemBridgeScripts.GitStagedDiffScript, workspacePath, entry.FilePath, cancellationToken);
            var unstagedDiff = await ExecuteDiffCommandAsync(config, UnixFileSystemBridgeScripts.GitUnstagedDiffScript, workspacePath, entry.FilePath, cancellationToken);
            return UnixFileSystemBridgeParsers.BuildDiffText(entry, stagedDiff, unstagedDiff);
        }

        private async Task<string> ExecuteDiffCommandAsync(
            WslAppServerConfigSnapshot config,
            string script,
            string workspacePath,
            string filePath,
            CancellationToken cancellationToken)
        {
            var result = await _executor.ExecuteAsync(
                config,
                script,
                "mycodex-read-git-diff",
                GitDiffTimeoutMilliseconds,
                cancellationToken,
                workspacePath,
                filePath);
            return result.ExitCode == 0 ? result.StandardOutput : string.Empty;
        }

        private static InvalidOperationException BuildWslCommandException(WslCommandExecutionResult result)
        {
            var message = result != null && !string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardError.Trim()
                : SharedLocalization.Text("WslFileSystemBridge.Error.CommandFailed", "WSL command failed.");
            return new InvalidOperationException(message);
        }
    }
}
