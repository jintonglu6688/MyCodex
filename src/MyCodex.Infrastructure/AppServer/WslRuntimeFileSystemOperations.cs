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
                false);
        }

        public Task<RuntimeFileSystemOperationResult> EnsureDirectoryAsync(string path)
        {
            return _inner.EnsureDirectoryAsync(path);
        }
    }
}
