using System;
using System.IO;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public static class RuntimeFileSystemPathRules
    {
        public static RuntimeFileSystemOperationResult ValidateName(string name)
        {
            var value = (name ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return RuntimeFileSystemOperationResult.Fail("Name is required.");
            }

            if (value == "." ||
                value == ".." ||
                value.IndexOf('\0') >= 0 ||
                value.IndexOf('/') >= 0 ||
                value.IndexOf('\\') >= 0)
            {
                return RuntimeFileSystemOperationResult.Fail("Name cannot contain path separators.");
            }

            return RuntimeFileSystemOperationResult.Ok(string.Empty);
        }

        public static string NormalizeName(string name)
        {
            return (name ?? string.Empty).Trim();
        }

        public static string CombineChildPath(string parentPath, string name)
        {
            var parent = (parentPath ?? string.Empty).Trim();
            var childName = NormalizeName(name);
            if (UsesUnixSeparators(parent))
            {
                return parent.TrimEnd('/') + "/" + childName;
            }

            return parent.TrimEnd('\\', '/') + "\\" + childName;
        }

        public static string GetSiblingPath(string path, string newName)
        {
            var normalizedPath = (path ?? string.Empty).Trim();
            var name = NormalizeName(newName);
            var separator = Math.Max(normalizedPath.LastIndexOf('/'), normalizedPath.LastIndexOf('\\'));
            if (separator < 0)
            {
                return name;
            }

            if (separator == 0 && normalizedPath[0] == '/')
            {
                return "/" + name;
            }

            return normalizedPath.Substring(0, separator + 1) + name;
        }

        public static RuntimeFileSystemOperationResult EnsureSafeDeleteTarget(string path)
        {
            var normalizedPath = (path ?? string.Empty).Trim().TrimEnd('\\', '/');
            if (normalizedPath.Length == 0)
            {
                return RuntimeFileSystemOperationResult.Fail("Path is required.");
            }

            var original = (path ?? string.Empty).Trim();
            if (original == "/" ||
                IsWindowsDriveRoot(original) ||
                string.Equals(normalizedPath, ".", StringComparison.Ordinal) ||
                string.Equals(normalizedPath, "..", StringComparison.Ordinal))
            {
                return RuntimeFileSystemOperationResult.Fail("Root paths cannot be deleted.");
            }

            return RuntimeFileSystemOperationResult.Ok(string.Empty);
        }

        public static bool UsesUnixSeparators(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.TrimStart().StartsWith("/", StringComparison.Ordinal) &&
                   path.IndexOf('\\') < 0;
        }

        private static bool IsWindowsDriveRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var value = path.Trim();
            if (value.Length == 2 &&
                char.IsLetter(value[0]) &&
                value[1] == ':')
            {
                return true;
            }

            if (value.Length == 3 &&
                char.IsLetter(value[0]) &&
                value[1] == ':' &&
                (value[2] == '\\' || value[2] == '/'))
            {
                return true;
            }

            try
            {
                var root = Path.GetPathRoot(value);
                return !string.IsNullOrWhiteSpace(root) &&
                       string.Equals(root.TrimEnd('\\', '/'), value.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
