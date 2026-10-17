using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class OfficeRuntimeFileSystem : IOfficeRuntimeFileSystem
    {
        private readonly IWslFileSystemBridge _wslFileSystemBridge;
        private readonly IRemoteMacFileSystemBridge _remoteFileSystemBridge;

        public OfficeRuntimeFileSystem()
            : this(new WslFileSystemBridge(), new RemoteMacFileSystemBridge())
        {
        }

        internal OfficeRuntimeFileSystem(
            IWslFileSystemBridge wslFileSystemBridge,
            IRemoteMacFileSystemBridge remoteFileSystemBridge)
        {
            _wslFileSystemBridge = wslFileSystemBridge ?? throw new ArgumentNullException(nameof(wslFileSystemBridge));
            _remoteFileSystemBridge = remoteFileSystemBridge ?? throw new ArgumentNullException(nameof(remoteFileSystemBridge));
        }

        public Task<string> ResolveHomeDirectoryAsync(AppConfigSnapshot config)
        {
            config = config ?? new AppConfigSnapshot();
            if (IsWsl(config))
            {
                return _wslFileSystemBridge.ResolveHomeDirectoryAsync(config.WslAppServer);
            }

            if (IsRemote(config))
            {
                return _remoteFileSystemBridge.ResolveHomeDirectoryAsync(config.GetSelectedRemoteUnixSshConfig());
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
            {
                throw new InvalidOperationException("The Windows user profile directory is unavailable.");
            }

            return Task.FromResult(Path.GetFullPath(userProfile));
        }

        public Task<bool> DirectoryExistsAsync(AppConfigSnapshot config, string path)
        {
            config = config ?? new AppConfigSnapshot();
            if (IsWsl(config))
            {
                return _wslFileSystemBridge.DirectoryExistsAsync(config.WslAppServer, path);
            }

            if (IsRemote(config))
            {
                return _remoteFileSystemBridge.DirectoryExistsAsync(config.GetSelectedRemoteUnixSshConfig(), path);
            }

            return Task.FromResult(!string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
        }

        public Task<RuntimeFileSystemOperationResult> EnsureDirectoryAsync(
            AppConfigSnapshot config,
            string path)
        {
            return CreateOperations(config).EnsureDirectoryAsync(path);
        }

        public Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(
            AppConfigSnapshot config,
            string path)
        {
            config = config ?? new AppConfigSnapshot();
            if (IsWsl(config))
            {
                return _wslFileSystemBridge.ListDirectoryAsync(config.WslAppServer, path);
            }

            if (IsRemote(config))
            {
                return _remoteFileSystemBridge.ListDirectoryAsync(config.GetSelectedRemoteUnixSshConfig(), path);
            }

            return Task.FromResult(ListLocalDirectory(path));
        }

        private static IRuntimeFileSystemOperations CreateOperations(AppConfigSnapshot config)
        {
            config = config ?? new AppConfigSnapshot();
            if (IsWsl(config))
            {
                return new WslRuntimeFileSystemOperations(config.WslAppServer);
            }

            if (IsRemote(config))
            {
                return new RemoteMacRuntimeFileSystemOperations(config.GetSelectedRemoteUnixSshConfig());
            }

            return new LocalRuntimeFileSystemOperations();
        }

        private static IReadOnlyList<RuntimeFileSystemEntrySnapshot> ListLocalDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                throw new InvalidOperationException("Local directory was not found.");
            }

            var entries = new List<RuntimeFileSystemEntrySnapshot>();
            foreach (var item in new DirectoryInfo(path).GetFileSystemInfos())
            {
                var file = item as FileInfo;
                entries.Add(new RuntimeFileSystemEntrySnapshot
                {
                    Name = item.Name,
                    FullPath = item.FullName,
                    IsDirectory = (item.Attributes & FileAttributes.Directory) != 0,
                    IsHidden = (item.Attributes & FileAttributes.Hidden) != 0,
                    Size = file == null ? (long?)null : file.Length,
                    ModifiedAt = item.LastWriteTime
                });
            }

            return entries;
        }

        private static bool IsWsl(AppConfigSnapshot config)
        {
            return config != null && string.Equals(
                config.CodexRuntimeSource,
                CodexRuntimeSources.Wsl,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRemote(AppConfigSnapshot config)
        {
            return config != null && CodexRuntimeSources.IsRemoteUnixSsh(config.CodexRuntimeSource);
        }
    }
}
