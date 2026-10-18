using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class UnixGitCloneService : IGitCloneService
    {
        private readonly UnixCommandInvoker _execute;
        private readonly string _operationPrefix;
        private readonly int _timeoutMilliseconds;

        internal UnixGitCloneService(UnixCommandInvoker execute, string operationPrefix, int timeoutMilliseconds)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _operationPrefix = operationPrefix ?? throw new ArgumentNullException(nameof(operationPrefix));
            _timeoutMilliseconds = timeoutMilliseconds;
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
            var result = _execute(
                UnixGitCloneScript.CloneScript,
                _operationPrefix + "-git-clone",
                _timeoutMilliseconds,
                repositoryUrl,
                parentDirectory,
                folderName,
                request.RecurseSubmodules ? "true" : "false").GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();

            return UnixGitCloneWorkflow.BuildResult(request, parentDirectory, folderName, result, progress);
        }
    }
}
