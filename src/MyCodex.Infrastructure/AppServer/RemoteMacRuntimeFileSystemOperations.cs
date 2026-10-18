using System;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class RemoteMacRuntimeFileSystemOperations : IRuntimeFileSystemOperations
    {
        private const int OperationTimeoutMilliseconds = 18000;

        private readonly RemoteMacSshConfigSnapshot _config;
        private readonly IRemoteSshCommandExecutor _executor;

        public RemoteMacRuntimeFileSystemOperations(RemoteMacSshConfigSnapshot config)
            : this(config, new RemoteSshCommandExecutor())
        {
        }

        internal RemoteMacRuntimeFileSystemOperations(RemoteMacSshConfigSnapshot config, IRemoteSshCommandExecutor executor)
        {
            _config = config ?? new RemoteMacSshConfigSnapshot();
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public Task<RuntimeFileSystemOperationResult> EnsureDirectoryAsync(string path)
        {
            return ExecuteAsync(
                UnixRuntimeFileSystemScripts.EnsureDirectory(true),
                "mycodex-remote-ensure-directory",
                "Folder ready.",
                path == null ? string.Empty : path.Trim());
        }

        public Task<RuntimeFileSystemOperationResult> CreateDirectoryAsync(string parentPath, string name)
        {
            return ExecuteNamedOperationAsync(
                UnixRuntimeFileSystemScripts.CreateDirectory(true),
                "mycodex-remote-create-directory",
                "Folder created.",
                parentPath,
                name);
        }

        public Task<RuntimeFileSystemOperationResult> CreateFileAsync(string parentPath, string name)
        {
            return ExecuteNamedOperationAsync(
                UnixRuntimeFileSystemScripts.CreateFile(true),
                "mycodex-remote-create-file",
                "File created.",
                parentPath,
                name);
        }

        public Task<RuntimeFileSystemOperationResult> RenameAsync(string path, string newName)
        {
            return ExecuteNamedOperationAsync(
                UnixRuntimeFileSystemScripts.Rename(true, UnixRuntimeRenameParentMode.ParameterExpansion),
                "mycodex-remote-rename",
                "Item renamed.",
                path,
                newName);
        }

        public async Task<RuntimeFileSystemOperationResult> DeleteAsync(string path, bool recursive)
        {
            var validation = RuntimeFileSystemPathRules.EnsureSafeDeleteTarget(path);
            if (!validation.Success)
            {
                return validation;
            }

            return await ExecuteAsync(
                UnixRuntimeFileSystemScripts.Delete(true),
                "mycodex-remote-delete",
                "Item deleted.",
                path == null ? string.Empty : path.Trim(),
                recursive ? "recursive" : "single").ConfigureAwait(false);
        }

        private async Task<RuntimeFileSystemOperationResult> ExecuteNamedOperationAsync(
            string script,
            string operationName,
            string successMessage,
            string path,
            string name)
        {
            var validation = RuntimeFileSystemPathRules.ValidateName(name);
            if (!validation.Success)
            {
                return validation;
            }

            return await ExecuteAsync(
                script,
                operationName,
                successMessage,
                path == null ? string.Empty : path.Trim(),
                RuntimeFileSystemPathRules.NormalizeName(name)).ConfigureAwait(false);
        }

        private async Task<RuntimeFileSystemOperationResult> ExecuteAsync(
            string script,
            string operationName,
            string successMessage,
            params string[] arguments)
        {
            try
            {
                var result = await _executor.ExecuteAsync(
                    _config,
                    script,
                    operationName,
                    OperationTimeoutMilliseconds,
                    arguments).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    return RuntimeFileSystemOperationResult.Ok(successMessage);
                }

                return RuntimeFileSystemOperationResult.Fail(TextValueHelper.FirstNonEmptyTrimmed(result.StandardError, result.StandardOutput, "Runtime file operation failed."));
            }
            catch (Exception ex)
            {
                return RuntimeFileSystemOperationResult.Fail(ex.Message);
            }
        }
    }
}
