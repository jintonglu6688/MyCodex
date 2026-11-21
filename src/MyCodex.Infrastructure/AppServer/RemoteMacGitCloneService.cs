using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class RemoteMacGitCloneService : IGitCloneService
    {
        private readonly IGitCloneService _inner;

        public RemoteMacGitCloneService(RemoteMacSshConfigSnapshot config)
            : this(config, new RemoteSshCommandExecutor())
        {
        }

        internal RemoteMacGitCloneService(RemoteMacSshConfigSnapshot config, IRemoteSshCommandExecutor executor)
        {
            _inner = new UnixGitCloneService(
                UnixCommandInvokers.ForRemoteCancellable(config, executor),
                "mycodex-remote",
                // 0 would fall back to the SSH executor's 10-second default; match the WSL clone budget.
                30 * 60 * 1000);
        }

        public Task<GitCloneResult> CloneAsync(
            GitCloneRequest request,
            IProgress<GitCloneProgress> progress,
            CancellationToken cancellationToken)
        {
            return _inner.CloneAsync(request, progress, cancellationToken);
        }
    }
}
