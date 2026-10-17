using System;
using System.Collections.Generic;
using System.IO;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Services
{
    public sealed class OfficeWorkspaceService
    {
        private const string DefaultOfficeFolderName = "MyCodex-office";

        private readonly Func<string> _documentsPathProvider;
        private readonly Func<string> _userProfilePathProvider;

        public OfficeWorkspaceService()
            : this(
                () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
        {
        }

        public OfficeWorkspaceService(Func<string> documentsPathProvider, Func<string> userProfilePathProvider)
        {
            _documentsPathProvider = documentsPathProvider ?? throw new ArgumentNullException(nameof(documentsPathProvider));
            _userProfilePathProvider = userProfilePathProvider ?? throw new ArgumentNullException(nameof(userProfilePathProvider));
        }

        public string ResolveOfficeRootPath(string configuredRootPath)
        {
            return ResolveOfficeRootPath(AppServerHostPlatform.Windows, configuredRootPath, null);
        }

        public string NormalizeConfiguredRootPath(
            AppServerHostPlatform hostPlatform,
            string configuredRootPath)
        {
            if (string.IsNullOrWhiteSpace(configuredRootPath))
            {
                return null;
            }

            if (hostPlatform == AppServerHostPlatform.Windows)
            {
                return Path.GetFullPath(configuredRootPath.Trim());
            }

            var trimmed = configuredRootPath.Trim().Trim('"');
            if (IsUnixHomeExpression(trimmed))
            {
                return trimmed;
            }

            if (!WorkspacePathUtilities.LooksLikeUnixAbsolutePath(trimmed))
            {
                throw new ArgumentException("Office root must be an absolute Unix path or home expression.", nameof(configuredRootPath));
            }

            return WorkspacePathUtilities.NormalizeUnixAbsolutePath(trimmed);
        }

        public string ResolveOfficeRootPath(
            AppServerHostPlatform hostPlatform,
            string configuredRootPath,
            string unixHomePath)
        {
            var normalizedConfiguredPath = NormalizeConfiguredRootPath(hostPlatform, configuredRootPath);
            if (hostPlatform == AppServerHostPlatform.Windows)
            {
                if (normalizedConfiguredPath != null)
                {
                    return normalizedConfiguredPath;
                }

                var documentsPath = _documentsPathProvider();
                if (!string.IsNullOrWhiteSpace(documentsPath))
                {
                    return Path.GetFullPath(Path.Combine(documentsPath, DefaultOfficeFolderName));
                }

                var userProfilePath = _userProfilePathProvider();
                if (!string.IsNullOrWhiteSpace(userProfilePath))
                {
                    return Path.GetFullPath(Path.Combine(userProfilePath, DefaultOfficeFolderName));
                }

                return Path.GetFullPath(Environment.CurrentDirectory);
            }

            if (normalizedConfiguredPath != null &&
                WorkspacePathUtilities.LooksLikeUnixAbsolutePath(normalizedConfiguredPath))
            {
                return normalizedConfiguredPath;
            }

            var normalizedHomePath = NormalizeUnixHomePath(unixHomePath);
            if (normalizedConfiguredPath == null)
            {
                return CombineUnixPath(normalizedHomePath, DefaultOfficeFolderName);
            }

            if (string.Equals(normalizedConfiguredPath, "~", StringComparison.Ordinal) ||
                string.Equals(normalizedConfiguredPath, "$HOME", StringComparison.Ordinal))
            {
                return normalizedHomePath;
            }

            var prefixLength = normalizedConfiguredPath.StartsWith("~/", StringComparison.Ordinal) ? 2 : 6;
            return CombineUnixPath(normalizedHomePath, normalizedConfiguredPath.Substring(prefixLength));
        }

        public WorkspaceInfo CreateOfficeWorkspace(string configuredRootPath)
        {
            var rootPath = ResolveOfficeRootPath(configuredRootPath);
            return CreateOfficeWorkspaceSnapshot("office:windows", "Office", rootPath);
        }

        public WorkspaceInfo CreateOfficeWorkspace(
            string runtimeScopeId,
            AppServerHostPlatform hostPlatform,
            string resolvedRootPath)
        {
            var rootPath = NormalizeResolvedRootPath(hostPlatform, resolvedRootPath);
            return CreateOfficeWorkspaceSnapshot(
                BuildOfficeWorkspaceId(runtimeScopeId),
                "Office",
                rootPath);
        }

        public WorkspaceInfo ResolveOfficeWorkspaceForImport(
            string configuredRootPath,
            string candidateWorkingDirectory,
            IEnumerable<WorkspaceInfo> knownOfficeWorkspaces)
        {
            EnsurePathIsWithinOfficeRoot(
                AppServerHostPlatform.Windows,
                ResolveOfficeRootPath(configuredRootPath),
                candidateWorkingDirectory);
            return CreateOfficeWorkspace(configuredRootPath);
        }

        public WorkspaceInfo ResolveOfficeWorkspaceForImport(
            string runtimeScopeId,
            AppServerHostPlatform hostPlatform,
            string resolvedRootPath,
            string candidateWorkingDirectory,
            IEnumerable<WorkspaceInfo> knownOfficeWorkspaces)
        {
            EnsurePathIsWithinOfficeRoot(
                hostPlatform,
                resolvedRootPath,
                candidateWorkingDirectory);
            return CreateOfficeWorkspace(runtimeScopeId, hostPlatform, resolvedRootPath);
        }

        private static void EnsurePathIsWithinOfficeRoot(
            AppServerHostPlatform hostPlatform,
            string resolvedRootPath,
            string candidateWorkingDirectory)
        {
            if (string.IsNullOrWhiteSpace(candidateWorkingDirectory))
            {
                throw new ArgumentException("Working directory is required.", nameof(candidateWorkingDirectory));
            }

            var rootPath = NormalizeResolvedRootPath(hostPlatform, resolvedRootPath);
            var candidatePath = NormalizeResolvedRootPath(hostPlatform, candidateWorkingDirectory);
            if (PathsEqual(hostPlatform, rootPath, candidatePath))
            {
                return;
            }

            var rootWithSeparator = AppendDirectorySeparator(hostPlatform, rootPath);
            var comparison = hostPlatform == AppServerHostPlatform.Windows
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!candidatePath.StartsWith(rootWithSeparator, comparison))
            {
                throw new ArgumentException("Working directory must be inside the Office root.", nameof(candidateWorkingDirectory));
            }
        }

        private static bool PathsEqual(
            AppServerHostPlatform hostPlatform,
            string firstPath,
            string secondPath)
        {
            return string.Equals(
                NormalizeResolvedRootPath(hostPlatform, firstPath),
                NormalizeResolvedRootPath(hostPlatform, secondPath),
                hostPlatform == AppServerHostPlatform.Windows
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }

        private static string AppendDirectorySeparator(AppServerHostPlatform hostPlatform, string path)
        {
            if (hostPlatform == AppServerHostPlatform.Windows)
            {
                return AppendDirectorySeparator(path);
            }

            return string.Equals(path, "/", StringComparison.Ordinal)
                ? path
                : path.TrimEnd('/') + "/";
        }

        private static WorkspaceInfo CreateOfficeWorkspaceSnapshot(string id, string name, string rootPath)
        {
            return new WorkspaceInfo
            {
                Id = id,
                Name = name,
                RootPath = rootPath,
                Kind = WorkspaceKind.Office,
                LastOpenedAt = DateTime.Now
            };
        }

        private static string BuildOfficeWorkspaceId(string runtimeScopeId)
        {
            if (string.IsNullOrWhiteSpace(runtimeScopeId))
            {
                throw new ArgumentException("A runtime scope ID is required.", nameof(runtimeScopeId));
            }

            return "office:" + runtimeScopeId.Trim().ToLowerInvariant();
        }

        private static string NormalizeResolvedRootPath(
            AppServerHostPlatform hostPlatform,
            string resolvedRootPath)
        {
            if (string.IsNullOrWhiteSpace(resolvedRootPath))
            {
                throw new ArgumentException("An absolute Office path is required.", nameof(resolvedRootPath));
            }

            if (hostPlatform == AppServerHostPlatform.Windows)
            {
                return Path.GetFullPath(resolvedRootPath.Trim());
            }

            var trimmed = resolvedRootPath.Trim().Trim('"');
            if (!WorkspacePathUtilities.LooksLikeUnixAbsolutePath(trimmed))
            {
                throw new ArgumentException("An absolute Unix Office path is required.", nameof(resolvedRootPath));
            }

            return WorkspacePathUtilities.NormalizeUnixAbsolutePath(trimmed);
        }

        private static string NormalizeUnixHomePath(string unixHomePath)
        {
            if (string.IsNullOrWhiteSpace(unixHomePath) ||
                !WorkspacePathUtilities.LooksLikeUnixAbsolutePath(unixHomePath))
            {
                throw new ArgumentException("A resolved absolute Unix home path is required.", nameof(unixHomePath));
            }

            return WorkspacePathUtilities.NormalizeUnixAbsolutePath(unixHomePath);
        }

        private static bool IsUnixHomeExpression(string path)
        {
            return string.Equals(path, "~", StringComparison.Ordinal) ||
                   path.StartsWith("~/", StringComparison.Ordinal) ||
                   string.Equals(path, "$HOME", StringComparison.Ordinal) ||
                   path.StartsWith("$HOME/", StringComparison.Ordinal);
        }

        private static string CombineUnixPath(string rootPath, string childPath)
        {
            return WorkspacePathUtilities.NormalizeUnixAbsolutePath(
                (string.Equals(rootPath, "/", StringComparison.Ordinal) ? string.Empty : rootPath.TrimEnd('/')) +
                "/" +
                childPath.TrimStart('/'));
        }

        private static string AppendDirectorySeparator(string path)
        {
            var trimmed = TrimTrailingDirectorySeparators(path);
            return trimmed + Path.DirectorySeparatorChar;
        }

        private static string TrimTrailingDirectorySeparators(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var root = Path.GetPathRoot(path);
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase)
                ? root
                : trimmed;
        }
    }
}
