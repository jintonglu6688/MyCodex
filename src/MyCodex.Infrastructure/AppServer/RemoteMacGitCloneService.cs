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
        private readonly RemoteMacSshConfigSnapshot _config;
        private readonly IRemoteSshCommandExecutor _executor;

        public RemoteMacGitCloneService(RemoteMacSshConfigSnapshot config)
            : this(config, new RemoteSshCommandExecutor())
        {
        }

        internal RemoteMacGitCloneService(RemoteMacSshConfigSnapshot config, IRemoteSshCommandExecutor executor)
        {
            _config = config ?? new RemoteMacSshConfigSnapshot();
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
                "mycodex-remote-git-clone",
                0,
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
