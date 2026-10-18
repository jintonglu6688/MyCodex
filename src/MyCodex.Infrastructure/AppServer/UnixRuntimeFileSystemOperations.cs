using System;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class UnixRuntimeFileSystemOperations : IRuntimeFileSystemOperations
    {
        private readonly UnixCommandInvoker _execute;
        private readonly string _operationPrefix;
        private readonly int _timeoutMilliseconds;
        private readonly bool _remoteShellPrefix;

        internal UnixRuntimeFileSystemOperations(
            UnixCommandInvoker execute,
            string operationPrefix,
            int timeoutMilliseconds,
            bool remoteShellPrefix)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _operationPrefix = operationPrefix ?? throw new ArgumentNullException(nameof(operationPrefix));
            _timeoutMilliseconds = timeoutMilliseconds;
            _remoteShellPrefix = remoteShellPrefix;
        }

        public async Task<RuntimeFileSystemOperationResult> EnsureDirectoryAsync(string path)
        {
            try
            {
                var result = await _execute(
                    UnixRuntimeFileSystemScripts.EnsureDirectory(_remoteShellPrefix),
                    _operationPrefix + "-ensure-directory",
                    _timeoutMilliseconds,
                    path == null ? string.Empty : path.Trim()).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    return RuntimeFileSystemOperationResult.Ok("Folder ready.");
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
