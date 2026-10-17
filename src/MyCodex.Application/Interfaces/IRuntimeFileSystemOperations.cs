using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IRuntimeFileSystemOperations
    {
        Task<RuntimeFileSystemOperationResult> EnsureDirectoryAsync(string path);

        Task<RuntimeFileSystemOperationResult> CreateDirectoryAsync(string parentPath, string name);

        Task<RuntimeFileSystemOperationResult> CreateFileAsync(string parentPath, string name);

        Task<RuntimeFileSystemOperationResult> RenameAsync(string path, string newName);

        Task<RuntimeFileSystemOperationResult> DeleteAsync(string path, bool recursive);
    }
}
