using System;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class UnixRuntimeFileSystemOperations : IRuntimeFileSystemOperations
    {
        private readonly UnixCommandInvoker _execute;
        private readonly string _operationPrefix;
        private readonly int _timeoutMilliseconds;
        private readonly bool _remoteShellPrefix;
        private readonly UnixRuntimeRenameParentMode _renameParentMode;

        internal UnixRuntimeFileSystemOperations(
            UnixCommandInvoker execute,
            string operationPrefix,
            int timeoutMilliseconds,
            bool remoteShellPrefix,
            UnixRuntimeRenameParentMode renameParentMode)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _operationPrefix = operationPrefix ?? throw new ArgumentNullException(nameof(operationPrefix));
            _timeoutMilliseconds = timeoutMilliseconds;
            _remoteShellPrefix = remoteShellPrefix;
            _renameParentMode = renameParentMode;
        }

        public Task<RuntimeFileSystemOperationResult> EnsureDirectoryAsync(string path)
        {
            return ExecuteAsync(
                UnixRuntimeFileSystemScripts.EnsureDirectory(_remoteShellPrefix),
                _operationPrefix + "-ensure-directory",
                "Folder ready.",
                path == null ? string.Empty : path.Trim());
        }

        public Task<RuntimeFileSystemOperationResult> CreateDirectoryAsync(string parentPath, string name)
        {
            return ExecuteNamedOperationAsync(
                UnixRuntimeFileSystemScripts.CreateDirectory(_remoteShellPrefix),
                _operationPrefix + "-create-directory",
                "Folder created.",
                parentPath,
                name);
        }

        public Task<RuntimeFileSystemOperationResult> CreateFileAsync(string parentPath, string name)
        {
            return ExecuteNamedOperationAsync(
                UnixRuntimeFileSystemScripts.CreateFile(_remoteShellPrefix),
                _operationPrefix + "-create-file",
                "File created.",
                parentPath,
                name);
        }

        public Task<RuntimeFileSystemOperationResult> RenameAsync(string path, string newName)
        {
            return ExecuteNamedOperationAsync(
                UnixRuntimeFileSystemScripts.Rename(_remoteShellPrefix, _renameParentMode),
                _operationPrefix + "-rename",
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
                UnixRuntimeFileSystemScripts.Delete(_remoteShellPrefix),
                _operationPrefix + "-delete",
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
                var result = await _execute(
                    script,
                    operationName,
                    _timeoutMilliseconds,
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
