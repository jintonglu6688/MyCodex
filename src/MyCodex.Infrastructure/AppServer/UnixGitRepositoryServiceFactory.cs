using System;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    public static class UnixGitRepositoryServiceFactory
    {
        public static IGitRepositoryService CreateForWsl(WslAppServerConfigSnapshot config)
        {
            return Create(new WslGitCommandRunner(config));
        }

        public static IGitRepositoryService CreateForRemoteMac(RemoteMacSshConfigSnapshot config)
        {
            return Create(new RemoteMacGitCommandRunner(config));
        }

        internal static IGitRepositoryService Create(IGitCommandRunner runner)
        {
            return new GitRepositoryService(runner, NormalizeRuntimeWorkspacePath, true, true, false);
        }

        private static string NormalizeRuntimeWorkspacePath(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.WorkspacePathRequired", "Workspace path cannot be empty."),
                    nameof(workspacePath));
            }

            return workspacePath.Trim().Replace('\\', '/');
        }
    }
}
