using System;
using System.IO;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class LocalRuntimeFileSystemOperations : IRuntimeFileSystemOperations
    {
        public Task<RuntimeFileSystemOperationResult> EnsureDirectoryAsync(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                {
                    return Task.FromResult(RuntimeFileSystemOperationResult.Fail("An absolute directory path is required."));
                }

                Directory.CreateDirectory(Path.GetFullPath(path.Trim()));
                return Task.FromResult(RuntimeFileSystemOperationResult.Ok("Folder ready."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(RuntimeFileSystemOperationResult.Fail(ex.Message));
            }
        }
    }
}
