using System;
using System.Collections.Generic;
using System.IO;

namespace MyCodex.Application.Services
{
    public static class TextEditableFilePolicy
    {
        private static readonly HashSet<string> TextExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".adoc",
                ".asax",
                ".ascx",
                ".ashx",
                ".asm",
                ".aspx",
                ".bat",
                ".bash",
                ".c",
                ".cc",
                ".cfg",
                ".cmd",
                ".cmake",
                ".conf",
                ".config",
                ".cpp",
                ".cs",
                ".cshtml",
                ".csproj",
                ".css",
                ".csv",
                ".csx",
                ".cxx",
                ".dockerignore",
                ".editorconfig",
                ".env",
                ".fs",
                ".fsproj",
                ".fsx",
                ".gitattributes",
                ".gitignore",
                ".gitmodules",
                ".go",
                ".gradle",
                ".graphql",
                ".h",
                ".hpp",
                ".htm",
                ".html",
                ".hxx",
                ".ini",
                ".java",
                ".js",
                ".json",
                ".jsonc",
                ".jsx",
                ".kt",
                ".kts",
                ".less",
                ".lock",
                ".log",
                ".lua",
                ".m",
                ".markdown",
                ".md",
                ".mdx",
                ".mk",
                ".mm",
                ".mjs",
                ".php",
                ".pl",
                ".pm",
                ".props",
                ".ps1",
                ".psd1",
                ".psm1",
                ".py",
                ".pyw",
                ".r",
                ".rb",
                ".resx",
                ".rs",
                ".rst",
                ".sass",
                ".scala",
                ".scss",
                ".sh",
                ".sln",
                ".sql",
                ".swift",
                ".targets",
                ".toml",
                ".ts",
                ".tsv",
                ".tsx",
                ".txt",
                ".vb",
                ".vbproj",
                ".xaml",
                ".xml",
                ".yaml",
                ".yml",
                ".zsh"
            };

        private static readonly HashSet<string> TextFileNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".env",
                ".gitattributes",
                ".gitignore",
                ".gitmodules",
                "AGENTS.md",
                "CHANGELOG",
                "CODEOWNERS",
                "CONTRIBUTING",
                "Dockerfile",
                "LICENSE",
                "Makefile",
                "NOTICE",
                "README"
            };

        // shortcut: a fixed list of types Windows runs rather than shows, add a type when its default action runs code.
        private static readonly HashSet<string> RunnableExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".bat", ".cmd", ".com", ".cpl", ".exe", ".hta", ".jar", ".js", ".jse", ".lnk", ".msc", ".msi", ".msp",
                ".pif", ".ps1", ".py", ".pyw", ".reg", ".scr", ".sh", ".url", ".vbe", ".vbs", ".wsf", ".wsh"
            };

        public static bool RunsWhenOpenedByWindows(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            // Windows ignores trailing dots and spaces, so "setup.exe." still runs.
            return RunnableExtensions.Contains(Path.GetExtension(path.Trim().TrimEnd('.', ' ')));
        }

        public static bool ShouldOpenWithTextEditor(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var fileName = Path.GetFileName(path.Trim());
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (TextFileNames.Contains(fileName))
            {
                return true;
            }

            var extension = Path.GetExtension(fileName);
            return !string.IsNullOrWhiteSpace(extension) && TextExtensions.Contains(extension);
        }
    }
}
