using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerFileSystemClient
    {
        Task<List<AppServerFileSystemEntry>> ReadDirectoryAsync(string path);

        Task<AppServerFileSystemReadResult> ReadFileAsync(string path);

        Task<AppServerFileSystemMetadata> GetMetadataAsync(string path);

        Task WriteFileAsync(string path, byte[] data);

        Task CreateDirectoryAsync(string path, bool? recursive = null);

        Task RemoveAsync(string path, bool? recursive = null, bool? force = null);

        Task CopyAsync(string sourcePath, string destinationPath, bool recursive);

        Task<AppServerFileSystemWatchResult> WatchFileSystemAsync(string path, string watchId);

        Task UnwatchFileSystemAsync(string watchId);
    }
}
