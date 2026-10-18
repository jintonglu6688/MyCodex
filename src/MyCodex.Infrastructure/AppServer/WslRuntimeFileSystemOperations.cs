using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class WslRuntimeFileSystemOperations : IRuntimeFileSystemOperations
    {
        private readonly IRuntimeFileSystemOperations _inner;

        public WslRuntimeFileSystemOperations(WslAppServerConfigSnapshot config)
            : this(config, new WslCommandExecutor())
        {
        }

        internal WslRuntimeFileSystemOperations(WslAppServerConfigSnapshot config, IWslCommandExecutor executor)
        {
            _inner = new UnixRuntimeFileSystemOperations(
                UnixCommandInvokers.ForWsl(config, executor),
                "mycodex-wsl",
                15000,
                false,
                UnixRuntimeRenameParentMode.Dirname);
        }

        public Task<RuntimeFileSystemOperationResult> EnsureDirectoryAsync(string path)
        {
            return _inner.EnsureDirectoryAsync(path);
        }

        public Task<RuntimeFileSystemOperationResult> CreateDirectoryAsync(string parentPath, string name)
        {
            return _inner.CreateDirectoryAsync(parentPath, name);
        }

        public Task<RuntimeFileSystemOperationResult> CreateFileAsync(string parentPath, string name)
        {
            return _inner.CreateFileAsync(parentPath, name);
        }

        public Task<RuntimeFileSystemOperationResult> RenameAsync(string path, string newName)
        {
            return _inner.RenameAsync(path, newName);
        }

        public Task<RuntimeFileSystemOperationResult> DeleteAsync(string path, bool recursive)
        {
            return _inner.DeleteAsync(path, recursive);
        }
    }
}
