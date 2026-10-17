using System;
using MyCodex.Application.Services;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerPathMappingPolicy
    {
        private readonly AppServerHostPlatform _hostPlatform;
        private readonly IAppServerPathMapper _mapper;

        public AppServerPathMappingPolicy(IAppServerPathMapper mapper)
            : this(mapper != null ? mapper.HostPlatform : AppServerHostPlatform.Windows, mapper)
        {
        }

        private AppServerPathMappingPolicy(AppServerHostPlatform hostPlatform, IAppServerPathMapper mapper)
        {
            _hostPlatform = hostPlatform;
            _mapper = mapper;
        }

        public AppServerHostPlatform HostPlatform
        {
            get { return _hostPlatform; }
        }

        public static AppServerPathMappingPolicy ForHostPlatform(AppServerHostPlatform hostPlatform)
        {
            return hostPlatform == AppServerHostPlatform.Wsl
                ? new AppServerPathMappingPolicy(new WindowsMountedDriveWslPathMapper())
                : new AppServerPathMappingPolicy(hostPlatform, null);
        }

        public string MapRequiredClientPathToServer(string path, string pathKind)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var trimmedPath = path.Trim();
            if (_mapper == null)
            {
                if (HostPlatform == AppServerHostPlatform.RemoteMac)
                {
                    if (WorkspacePathUtilities.LooksLikeUnixAbsolutePath(trimmedPath))
                    {
                        return WorkspacePathUtilities.NormalizeUnixAbsolutePath(trimmedPath);
                    }

                    throw new InvalidOperationException(string.Format(
                        SharedLocalization.Text(
                            "AppServerPathMappingPolicy.Error.PathNotMappableToRemoteMac.Template",
                            "Cannot map {0} path to Remote Unix: {1}. Use an absolute Unix path such as /home/name/repo or /Users/name/repo."),
                        string.IsNullOrWhiteSpace(pathKind) ? "requested" : pathKind,
                        trimmedPath));
                }

                return trimmedPath;
            }

            string mappedPath;
            if (_mapper.TryMapToServerPath(trimmedPath, out mappedPath))
            {
                return mappedPath;
            }

            if (HostPlatform == AppServerHostPlatform.Wsl &&
                WorkspacePathUtilities.LooksLikeUnixAbsolutePath(trimmedPath))
            {
                return WorkspacePathUtilities.NormalizeUnixAbsolutePath(trimmedPath);
            }

            throw new InvalidOperationException(string.Format(
                SharedLocalization.Text(
                    "AppServerPathMappingPolicy.Error.PathNotMappableToWsl.Template",
                    "Cannot map {0} path to WSL: {1}. Use a Windows drive path such as E:\\Repo or an absolute WSL path such as /home/user/repo."),
                string.IsNullOrWhiteSpace(pathKind) ? "requested" : pathKind,
                trimmedPath));
        }

        public string MapClientOrServerPathToServer(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var trimmedPath = path.Trim();
            if (_mapper == null)
            {
                if (HostPlatform == AppServerHostPlatform.RemoteMac &&
                    WorkspacePathUtilities.LooksLikeUnixAbsolutePath(trimmedPath))
                {
                    return WorkspacePathUtilities.NormalizeUnixAbsolutePath(trimmedPath);
                }

                return trimmedPath;
            }

            string mappedPath;
            if (_mapper.TryMapToServerPath(trimmedPath, out mappedPath))
            {
                return mappedPath;
            }

            return HostPlatform == AppServerHostPlatform.Wsl &&
                   WorkspacePathUtilities.LooksLikeUnixAbsolutePath(trimmedPath)
                ? WorkspacePathUtilities.NormalizeUnixAbsolutePath(trimmedPath)
                : trimmedPath;
        }

        public string MapServerPathToClient(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || _mapper == null)
            {
                return path;
            }

            string mappedPath;
            return _mapper.TryMapToClientPath(path.Trim(), out mappedPath)
                ? mappedPath
                : path;
        }
    }
}
