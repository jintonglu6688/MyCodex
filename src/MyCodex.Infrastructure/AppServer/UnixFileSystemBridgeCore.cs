using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class UnixFileSystemBridgeProfile
    {
        public string TargetLabel { get; set; }

        public Func<string> CommandFailedMessage { get; set; }

        public string ListDirectoryScript { get; set; }

        public string DeleteCodexSessionScript { get; set; }

        public string ResolveHomeOperation { get; set; }

        public string TestDirectoryOperation { get; set; }

        public string TestFileOperation { get; set; }

        public string ListDirectoryOperation { get; set; }

        public string DeleteCodexSessionOperation { get; set; }

        public string ReadGitInfoOperation { get; set; }

        public int ProbeTimeoutMilliseconds { get; set; }

        public int ListDirectoryTimeoutMilliseconds { get; set; }

        public int DeleteTimeoutMilliseconds { get; set; }

        public int GitInfoTimeoutMilliseconds { get; set; }
    }

    internal sealed class UnixFileSystemBridgeCore
    {
        private const int UnsupportedSessionPathExitCode = 73;
        private const int DirectoryNotFoundExitCode = 66;

        private readonly UnixFileSystemBridgeProfile _profile;

        internal UnixFileSystemBridgeCore(UnixFileSystemBridgeProfile profile)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        public async Task<string> ResolveHomeDirectoryAsync(UnixCommandInvoker execute)
        {
            var result = await execute(
                UnixFileSystemBridgeScripts.ResolveHomeDirectoryScript,
                _profile.ResolveHomeOperation,
                _profile.ProbeTimeoutMilliseconds).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw BuildCommandException(result);
            }

            var homePath = (result.StandardOutput ?? string.Empty).Trim();
            if (homePath.IndexOf('\r') >= 0 ||
                homePath.IndexOf('\n') >= 0 ||
                !WorkspacePathUtilities.LooksLikeUnixAbsolutePath(homePath))
            {
                throw new InvalidOperationException(_profile.TargetLabel + " home directory was not returned as an absolute path.");
            }

            return WorkspacePathUtilities.NormalizeUnixAbsolutePath(homePath);
        }

        public Task<bool> DirectoryExistsAsync(UnixCommandInvoker execute, string path)
        {
            return TestPathAsync(execute, UnixFileSystemBridgeScripts.DirectoryExistsScript, _profile.TestDirectoryOperation, path);
        }

        public Task<bool> FileExistsAsync(UnixCommandInvoker execute, string path)
        {
            return TestPathAsync(execute, UnixFileSystemBridgeScripts.FileExistsScript, _profile.TestFileOperation, path);
        }

        public async Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(UnixCommandInvoker execute, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(_profile.TargetLabel + " directory path cannot be empty.", nameof(path));
            }

            var result = await execute(
                _profile.ListDirectoryScript,
                _profile.ListDirectoryOperation,
                _profile.ListDirectoryTimeoutMilliseconds,
                path.Trim()).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return UnixFileSystemBridgeParsers.ParseDirectoryListing(result.StandardOutput);
            }

            if (result.ExitCode == DirectoryNotFoundExitCode)
            {
                throw new InvalidOperationException(_profile.TargetLabel + " directory was not found.");
            }

            throw BuildCommandException(result);
        }

        public async Task DeleteCodexSessionPathAsync(UnixCommandInvoker execute, string sessionPath)
        {
            if (string.IsNullOrWhiteSpace(sessionPath))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("AppServerClient.Error.ThreadFilePathRequired", "Thread file path cannot be empty."),
                    nameof(sessionPath));
            }

            var result = await execute(
                _profile.DeleteCodexSessionScript,
                _profile.DeleteCodexSessionOperation,
                _profile.DeleteTimeoutMilliseconds,
                sessionPath.Trim()).ConfigureAwait(false);
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

            throw BuildCommandException(result);
        }

        public async Task<WorkspaceGitInfoSnapshot> ReadGitInfoAsync(UnixCommandInvoker execute, string workspacePath)
        {
            var snapshot = new WorkspaceGitInfoSnapshot();
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return snapshot;
            }

            var result = await execute(
                UnixFileSystemBridgeScripts.GitInfoScript,
                _profile.ReadGitInfoOperation,
                _profile.GitInfoTimeoutMilliseconds,
                workspacePath.Trim()).ConfigureAwait(false);
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

        public InvalidOperationException BuildCommandException(UnixCommandResult result)
        {
            var message = result != null && !string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardError.Trim()
                : _profile.CommandFailedMessage();
            return new InvalidOperationException(message);
        }

        private async Task<bool> TestPathAsync(UnixCommandInvoker execute, string script, string operationName, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var result = await execute(
                script,
                operationName,
                _profile.ProbeTimeoutMilliseconds,
                path.Trim()).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return true;
            }

            if (result.ExitCode == 1)
            {
                return false;
            }

            throw BuildCommandException(result);
        }
    }
}
