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
        private readonly WslAppServerConfigSnapshot _config;
        private readonly IWslCommandExecutor _executor;

        public WslGitCloneService(WslAppServerConfigSnapshot config)
            : this(config, new WslCommandExecutor())
        {
        }

        internal WslGitCloneService(WslAppServerConfigSnapshot config, IWslCommandExecutor executor)
        {
            _config = config ?? new WslAppServerConfigSnapshot();
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public Task<GitCloneResult> CloneAsync(
            GitCloneRequest request,
            IProgress<GitCloneProgress> progress,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return Task.Run(() => Clone(request, progress, cancellationToken), cancellationToken);
        }

        private GitCloneResult Clone(
            GitCloneRequest request,
            IProgress<GitCloneProgress> progress,
            CancellationToken cancellationToken)
        {
            UnixGitCloneWorkflow.ValidateRequest(request, out var repositoryUrl, out var parentDirectory, out var folderName);
            cancellationToken.ThrowIfCancellationRequested();
            var result = _executor.ExecuteAsync(
                _config,
                UnixGitCloneScript.CloneScript,
                "mycodex-wsl-git-clone",
                30 * 60 * 1000,
                repositoryUrl,
                parentDirectory,
                folderName,
                request.RecurseSubmodules ? "true" : "false").GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();

            return UnixGitCloneWorkflow.BuildResult(
                request,
                parentDirectory,
                folderName,
                new UnixGitCloneCommandResult
                {
                    ExitCode = result.ExitCode,
                    StandardOutput = result.StandardOutput,
                    StandardError = result.StandardError
                },
                progress);
        }
    }
}
