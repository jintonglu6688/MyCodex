using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    public interface IRemoteMacFileSystemBridge
    {
        Task<string> ResolveHomeDirectoryAsync(RemoteMacSshConfigSnapshot config);

        Task<bool> DirectoryExistsAsync(RemoteMacSshConfigSnapshot config, string path);

        Task<bool> FileExistsAsync(RemoteMacSshConfigSnapshot config, string path);

        Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(RemoteMacSshConfigSnapshot config, string path);

        Task DeleteCodexSessionPathAsync(RemoteMacSshConfigSnapshot config, string sessionPath);

        Task<string> ReadTextFileAsync(RemoteMacSshConfigSnapshot config, string path);

        Task<WorkspaceGitInfoSnapshot> ReadGitInfoAsync(RemoteMacSshConfigSnapshot config, string workspacePath);
    }
}
