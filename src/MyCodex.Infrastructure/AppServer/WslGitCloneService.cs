using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class WslGitCloneService : IGitCloneService
    {
        private readonly IGitCloneService _inner;

        public WslGitCloneService(WslAppServerConfigSnapshot config)
            : this(config, new WslCommandExecutor())
        {
        }

        internal WslGitCloneService(WslAppServerConfigSnapshot config, IWslCommandExecutor executor)
        {
            _inner = new UnixGitCloneService(
                UnixCommandInvokers.ForWsl(config, executor),
                "mycodex-wsl",
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
