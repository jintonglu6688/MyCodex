using System;
using System.Globalization;
using System.IO;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public class WorkspaceService
    {
        public WorkspaceInfo CreateFromPath(string workspacePath)
        {
            return CreateFromPath(workspacePath, true);
        }

        public WorkspaceInfo CreateFromPath(string workspacePath, bool requireExisting)
        {
            return CreateFromPath(workspacePath, requireExisting, Directory.Exists, ReadGitInfo);
        }

        public WorkspaceInfo CreateFromPath(
            string workspacePath,
            bool requireExisting,
            Func<string, bool> directoryExists,
            Func<string, WorkspaceGitInfoSnapshot> readGitInfo)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("WorkspaceService.Error.WorkspacePathRequired", "Workspace path cannot be empty."),
                    nameof(workspacePath));
            }

            directoryExists = directoryExists ?? Directory.Exists;
            readGitInfo = readGitInfo ?? ReadGitInfo;

            var normalizedPath = NormalizeWorkspacePath(workspacePath);
            if (requireExisting && !directoryExists(normalizedPath))
            {
                throw new DirectoryNotFoundException(
                    SharedLocalization.Text("WorkspaceService.Error.WorkspaceNotFound", "The specified workspace does not exist."));
            }

            var directoryName = WorkspacePathUtilities.GetWorkspaceName(normalizedPath);
            return new WorkspaceInfo
            {
                Id = BuildWorkspaceId(normalizedPath),
                Name = string.IsNullOrWhiteSpace(directoryName) ? normalizedPath : directoryName,
                RootPath = normalizedPath,
                GitBranch = ResolveGitBranch(normalizedPath, requireExisting, directoryExists, readGitInfo),
                LastOpenedAt = DateTime.Now
            };
        }

        public string NormalizeWorkspacePath(string workspacePath)
        {
            var path = workspacePath.Trim();
            const string windowsLongPathPrefix = @"\\?\";
            if (path.StartsWith(windowsLongPathPrefix, StringComparison.Ordinal))
            {
                path = path.Substring(windowsLongPathPrefix.Length);
            }

            if (WorkspacePathUtilities.LooksLikeUnixAbsolutePath(path))
            {
                return WorkspacePathUtilities.NormalizeUnixAbsolutePath(path);
            }

            return Path.GetFullPath(path);
        }

        public WorkspaceGitInfoSnapshot ReadGitInfo(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("WorkspaceService.Error.WorkspacePathRequired", "Workspace path cannot be empty."),
                    nameof(workspacePath));
            }

            var normalizedPath = NormalizeWorkspacePath(workspacePath);
            var snapshot = new WorkspaceGitInfoSnapshot();
            if (!Directory.Exists(normalizedPath))
            {
                return snapshot;
            }

            var gitDirectory = ResolveGitDirectory(normalizedPath);
            if (string.IsNullOrWhiteSpace(gitDirectory))
            {
                return snapshot;
            }

            snapshot.IsGitRepository = true;
            snapshot.OriginUrl = ResolveGitOriginUrl(gitDirectory);

            var headFilePath = Path.Combine(gitDirectory, "HEAD");
            if (!File.Exists(headFilePath))
            {
                return snapshot;
            }

            var headContent = File.ReadAllText(headFilePath).Trim();
            const string refPrefix = "ref:";
            if (headContent.StartsWith(refPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var reference = headContent.Substring(refPrefix.Length).Trim();
                const string headsPrefix = "refs/heads/";
                snapshot.Branch = reference.StartsWith(headsPrefix, StringComparison.OrdinalIgnoreCase)
                    ? reference.Substring(headsPrefix.Length)
                    : reference;
                snapshot.Sha = ResolveGitReferenceValue(gitDirectory, reference);
                return snapshot;
            }

            if (headContent.Length > 0)
            {
                snapshot.Sha = headContent;
                snapshot.Branch = headContent.Length >= 8
                    ? "detached@" + headContent.Substring(0, 8)
                    : "detached";
            }

            return snapshot;
        }

        private static string BuildWorkspaceId(string normalizedPath)
        {
            var workspaceId = normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return workspaceId.ToLower(CultureInfo.InvariantCulture);
        }

        private static string ResolveGitBranch(
            string workspacePath,
            bool requireExisting,
            Func<string, bool> directoryExists,
            Func<string, WorkspaceGitInfoSnapshot> readGitInfo)
        {
            if (!directoryExists(workspacePath))
            {
                return requireExisting
                    ? SharedLocalization.Text("WorkspaceService.Value.PathUnavailable", "(Path unavailable)")
                    : SharedLocalization.Text("WorkspaceService.Value.HistoricalWorkspace", "(Historical workspace)");
            }

            var gitInfo = readGitInfo(workspacePath);
            if (gitInfo == null || !gitInfo.IsGitRepository)
            {
                return SharedLocalization.Text("WorkspaceService.Value.NotGitRepository", "(Not a Git repository)");
            }

            if (string.IsNullOrWhiteSpace(gitInfo.Branch))
            {
                return SharedLocalization.Text("WorkspaceService.Value.UnknownBranch", "(Unknown branch)");
            }

            return gitInfo.Branch;
        }

        private static string ResolveGitDirectory(string workspacePath)
        {
            var dotGitPath = Path.Combine(workspacePath, ".git");
            if (Directory.Exists(dotGitPath))
            {
                return dotGitPath;
            }

            if (!File.Exists(dotGitPath))
            {
                return null;
            }

            var gitPointer = File.ReadAllText(dotGitPath).Trim();
            const string gitDirPrefix = "gitdir:";
            if (!gitPointer.StartsWith(gitDirPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relativePath = gitPointer.Substring(gitDirPrefix.Length).Trim();
            return Path.GetFullPath(Path.Combine(workspacePath, relativePath));
        }

        private static string ResolveGitOriginUrl(string gitDirectory)
        {
            var configFilePath = ResolveGitConfigPath(gitDirectory);
            if (string.IsNullOrWhiteSpace(configFilePath) || !File.Exists(configFilePath))
            {
                return null;
            }

            var lines = File.ReadAllLines(configFilePath);
            var isOriginSection = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    isOriginSection = string.Equals(line, "[remote \"origin\"]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!isOriginSection)
                {
                    continue;
                }

                const string urlPrefix = "url";
                if (!line.StartsWith(urlPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex < 0)
                {
                    continue;
                }

                var value = line.Substring(separatorIndex + 1).Trim();
                return value.Length == 0 ? null : value;
            }

            return null;
        }

        private static string ResolveGitConfigPath(string gitDirectory)
        {
            var directConfigPath = Path.Combine(gitDirectory, "config");
            if (File.Exists(directConfigPath))
            {
                return directConfigPath;
            }

            var commonDirectory = ResolveGitCommonDirectory(gitDirectory);
            if (string.IsNullOrWhiteSpace(commonDirectory))
            {
                return null;
            }

            var commonConfigPath = Path.Combine(commonDirectory, "config");
            return File.Exists(commonConfigPath) ? commonConfigPath : null;
        }

        private static string ResolveGitReferenceValue(string gitDirectory, string reference)
        {
            if (string.IsNullOrWhiteSpace(gitDirectory) || string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }

            var referencePath = reference.Replace('/', Path.DirectorySeparatorChar);
            var directReferencePath = Path.Combine(gitDirectory, referencePath);
            if (File.Exists(directReferencePath))
            {
                var directValue = File.ReadAllText(directReferencePath).Trim();
                return directValue.Length == 0 ? null : directValue;
            }

            var commonDirectory = ResolveGitCommonDirectory(gitDirectory);
            if (!string.IsNullOrWhiteSpace(commonDirectory))
            {
                var commonReferencePath = Path.Combine(commonDirectory, referencePath);
                if (File.Exists(commonReferencePath))
                {
                    var commonValue = File.ReadAllText(commonReferencePath).Trim();
                    return commonValue.Length == 0 ? null : commonValue;
                }
            }

            return ResolvePackedReferenceValue(gitDirectory, reference, commonDirectory);
        }

        private static string ResolvePackedReferenceValue(string gitDirectory, string reference, string commonDirectory)
        {
            var packedRefsPaths = new[]
            {
                Path.Combine(gitDirectory, "packed-refs"),
                string.IsNullOrWhiteSpace(commonDirectory) ? null : Path.Combine(commonDirectory, "packed-refs")
            };

            for (var i = 0; i < packedRefsPaths.Length; i++)
            {
                var packedRefsPath = packedRefsPaths[i];
                if (string.IsNullOrWhiteSpace(packedRefsPath) || !File.Exists(packedRefsPath))
                {
                    continue;
                }

                foreach (var rawLine in File.ReadAllLines(packedRefsPath, Encoding.UTF8))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("^", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf(' ');
                    if (separatorIndex <= 0 || separatorIndex >= line.Length - 1)
                    {
                        continue;
                    }

                    if (!string.Equals(line.Substring(separatorIndex + 1), reference, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var value = line.Substring(0, separatorIndex).Trim();
                    return value.Length == 0 ? null : value;
                }
            }

            return null;
        }

        private static string ResolveGitCommonDirectory(string gitDirectory)
        {
            if (string.IsNullOrWhiteSpace(gitDirectory))
            {
                return null;
            }

            var commonDirFilePath = Path.Combine(gitDirectory, "commondir");
            if (!File.Exists(commonDirFilePath))
            {
                return gitDirectory;
            }

            var relativePath = File.ReadAllText(commonDirFilePath).Trim();
            if (relativePath.Length == 0)
            {
                return gitDirectory;
            }

            return Path.GetFullPath(Path.Combine(gitDirectory, relativePath));
        }
    }
}
