using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IRuntimeFileSystemOperations
    {
        Task<RuntimeFileSystemOperationResult> EnsureDirectoryAsync(string path);
    }
}
