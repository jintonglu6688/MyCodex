using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    public interface IWslFileSystemBridge
    {
        Task<string> ResolveHomeDirectoryAsync(WslAppServerConfigSnapshot config);

        Task<bool> DirectoryExistsAsync(WslAppServerConfigSnapshot config, string path);

        Task<bool> FileExistsAsync(WslAppServerConfigSnapshot config, string path);

        Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(WslAppServerConfigSnapshot config, string path);

        Task DeleteCodexSessionPathAsync(WslAppServerConfigSnapshot config, string sessionPath);

        Task<WorkspaceGitInfoSnapshot> ReadGitInfoAsync(WslAppServerConfigSnapshot config, string workspacePath);
    }
}
