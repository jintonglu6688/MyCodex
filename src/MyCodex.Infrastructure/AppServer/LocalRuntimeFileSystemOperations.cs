using System;
using System.IO;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;

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

        public Task<RuntimeFileSystemOperationResult> CreateDirectoryAsync(string parentPath, string name)
        {
            try
            {
                var validation = RuntimeFileSystemPathRules.ValidateName(name);
                if (!validation.Success)
                {
                    return Task.FromResult(validation);
                }

                if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
                {
                    return Task.FromResult(RuntimeFileSystemOperationResult.Fail("Parent directory was not found."));
                }

                var targetPath = RuntimeFileSystemPathRules.CombineChildPath(parentPath, name);
                if (File.Exists(targetPath) || Directory.Exists(targetPath))
                {
                    return Task.FromResult(RuntimeFileSystemOperationResult.Fail("Target already exists."));
                }

                Directory.CreateDirectory(targetPath);
                return Task.FromResult(RuntimeFileSystemOperationResult.Ok("Folder created."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(RuntimeFileSystemOperationResult.Fail(ex.Message));
            }
        }

        public Task<RuntimeFileSystemOperationResult> CreateFileAsync(string parentPath, string name)
        {
            try
            {
                var validation = RuntimeFileSystemPathRules.ValidateName(name);
                if (!validation.Success)
                {
                    return Task.FromResult(validation);
                }

                if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
                {
                    return Task.FromResult(RuntimeFileSystemOperationResult.Fail("Parent directory was not found."));
                }

                var targetPath = RuntimeFileSystemPathRules.CombineChildPath(parentPath, name);
                using (new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write))
                {
                }

                return Task.FromResult(RuntimeFileSystemOperationResult.Ok("File created."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(RuntimeFileSystemOperationResult.Fail(ex.Message));
            }
        }

        public Task<RuntimeFileSystemOperationResult> RenameAsync(string path, string newName)
        {
            try
            {
                var validation = RuntimeFileSystemPathRules.ValidateName(newName);
                if (!validation.Success)
                {
                    return Task.FromResult(validation);
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    return Task.FromResult(RuntimeFileSystemOperationResult.Fail("Path is required."));
                }

                var targetPath = RuntimeFileSystemPathRules.GetSiblingPath(path, newName);
                if (Directory.Exists(path))
                {
                    Directory.Move(path, targetPath);
                    return Task.FromResult(RuntimeFileSystemOperationResult.Ok("Item renamed."));
                }

                if (File.Exists(path))
                {
                    File.Move(path, targetPath);
                    return Task.FromResult(RuntimeFileSystemOperationResult.Ok("Item renamed."));
                }

                return Task.FromResult(RuntimeFileSystemOperationResult.Fail("Path was not found."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(RuntimeFileSystemOperationResult.Fail(ex.Message));
            }
        }

        public Task<RuntimeFileSystemOperationResult> DeleteAsync(string path, bool recursive)
        {
            try
            {
                var validation = RuntimeFileSystemPathRules.EnsureSafeDeleteTarget(path);
                if (!validation.Success)
                {
                    return Task.FromResult(validation);
                }

                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive);
                    return Task.FromResult(RuntimeFileSystemOperationResult.Ok("Item deleted."));
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                    return Task.FromResult(RuntimeFileSystemOperationResult.Ok("Item deleted."));
                }

                return Task.FromResult(RuntimeFileSystemOperationResult.Fail("Path was not found."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(RuntimeFileSystemOperationResult.Fail(ex.Message));
            }
        }
    }
}
