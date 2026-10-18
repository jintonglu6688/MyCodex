using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class WslGitSshKeyService : IGitSshKeyService
    {
        private readonly IGitSshKeyService _inner;

        public WslGitSshKeyService(WslAppServerConfigSnapshot config)
            : this(config, new WslCommandExecutor())
        {
        }

        internal WslGitSshKeyService(WslAppServerConfigSnapshot config, IWslCommandExecutor executor)
        {
            _inner = new UnixGitSshKeyService(
                UnixCommandInvokers.ForWsl(config, executor),
                "mycodex-wsl");
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
