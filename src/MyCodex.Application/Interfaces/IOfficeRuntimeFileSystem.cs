using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Interfaces
{
    public interface IOfficeRuntimeFileSystem
    {
        Task<string> ResolveHomeDirectoryAsync(AppConfigSnapshot config);

        Task<bool> DirectoryExistsAsync(AppConfigSnapshot config, string path);

        Task<RuntimeFileSystemOperationResult> EnsureDirectoryAsync(
            AppConfigSnapshot config,
            string path);

        Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(
            AppConfigSnapshot config,
            string path);
    }
}
