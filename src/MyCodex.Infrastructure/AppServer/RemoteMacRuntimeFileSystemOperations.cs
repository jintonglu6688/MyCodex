using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class RemoteMacRuntimeFileSystemOperations : IRuntimeFileSystemOperations
    {
        private readonly IRuntimeFileSystemOperations _inner;

        public RemoteMacRuntimeFileSystemOperations(RemoteMacSshConfigSnapshot config)
            : this(config, new RemoteSshCommandExecutor())
        {
        }

        internal RemoteMacRuntimeFileSystemOperations(RemoteMacSshConfigSnapshot config, IRemoteSshCommandExecutor executor)
        {
            _inner = new UnixRuntimeFileSystemOperations(
                UnixCommandInvokers.ForRemote(config, executor),
                "mycodex-remote",
                18000,
                true);
        }

        public Task<RuntimeFileSystemOperationResult> EnsureDirectoryAsync(string path)
        {
            return _inner.EnsureDirectoryAsync(path);
        }
    }
}
