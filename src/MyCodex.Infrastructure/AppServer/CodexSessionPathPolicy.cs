using System;
using System.Collections.Generic;
using System.IO;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class CodexSessionPathPolicy
    {
        internal static string ValidateDeletePath(
            string sessionPath,
            IEnumerable<string> allowedSessionRoots)
        {
            var candidatePath = NormalizeCandidatePath(sessionPath);
            var authorizedRoot = FindAuthorizedRoot(candidatePath, allowedSessionRoots);
            if (authorizedRoot == null)
            {
                throw new InvalidOperationException(L(
                    "AppServerClient.Error.ThreadFilePathUnsupported",
                    "Current thread file path is outside supported Codex state directories and cannot be fully removed."));
            }

            RejectReparsePoints(authorizedRoot, candidatePath);
            return candidatePath;
        }

        private static string NormalizeCandidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(L(
                    "AppServerClient.Error.ThreadFilePathUnsupported",
                    "Current thread file path is outside supported Codex state directories and cannot be fully removed."));
            }

            try
            {
                return AppServerPathUtilities.NormalizeLocalPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException)
            {
                throw new InvalidOperationException(L(
                    "AppServerClient.Error.ThreadFilePathUnsupported",
                    "Current thread file path is outside supported Codex state directories and cannot be fully removed."), ex);
            }
        }

        private static string FindAuthorizedRoot(
            string candidatePath,
            IEnumerable<string> allowedSessionRoots)
        {
            if (allowedSessionRoots == null)
            {
                return null;
            }

            foreach (var rootPath in allowedSessionRoots)
            {
                if (string.IsNullOrWhiteSpace(rootPath))
                {
                    continue;
                }

                string normalizedRoot;
                try
                {
                    normalizedRoot = AppServerPathUtilities.NormalizeLocalPath(rootPath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch (Exception ex) when (
                    ex is ArgumentException ||
                    ex is NotSupportedException ||
                    ex is PathTooLongException)
                {
                    continue;
                }

                if (candidatePath.StartsWith(
                    normalizedRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return normalizedRoot;
                }
            }

            return null;
        }

        private static void RejectReparsePoints(string rootPath, string candidatePath)
        {
            var currentPath = rootPath;
            ThrowIfReparsePoint(currentPath);

            var relativePath = candidatePath.Substring(rootPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parts = relativePath.Split(new[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                currentPath = Path.Combine(currentPath, part);
                ThrowIfReparsePoint(currentPath);
            }
        }

        private static void ThrowIfReparsePoint(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    throw CreateReparsePointException();
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(L(
                    "AppServerClient.Error.ThreadFilePathContainsReparsePoint",
                    "Current thread file path contains a reparse point and cannot be safely removed."), ex);
            }
        }

        private static InvalidOperationException CreateReparsePointException()
        {
            return new InvalidOperationException(L(
                "AppServerClient.Error.ThreadFilePathContainsReparsePoint",
                "Current thread file path contains a reparse point and cannot be safely removed."));
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }
    }
}
