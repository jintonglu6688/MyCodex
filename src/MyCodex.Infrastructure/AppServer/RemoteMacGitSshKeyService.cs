using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class RemoteMacGitSshKeyService : IGitSshKeyService
    {
        private readonly IGitSshKeyService _inner;

        public RemoteMacGitSshKeyService(RemoteMacSshConfigSnapshot config)
            : this(config, new RemoteSshCommandExecutor())
        {
        }

        internal RemoteMacGitSshKeyService(RemoteMacSshConfigSnapshot config, IRemoteSshCommandExecutor executor)
        {
            _inner = new UnixGitSshKeyService(
                UnixCommandInvokers.ForRemote(config, executor),
                "mycodex-remote");
        }

        public Task<GitSshKeySnapshot> ReadKeysAsync(CancellationToken cancellationToken)
        {
            return _inner.ReadKeysAsync(cancellationToken);
        }

        public Task<GitSshTestResult> TestGitHubSshAsync(CancellationToken cancellationToken)
        {
            return _inner.TestGitHubSshAsync(cancellationToken);
        }
    }
}
