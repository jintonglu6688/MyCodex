using System;
using System.IO;
using System.Text;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public sealed class ProjectInstructionsService
    {
        public const string FileName = "AGENTS.md";

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public ProjectInstructionsStatus ReadLocalStatus(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                throw new ArgumentException("Workspace path is required.", nameof(workspacePath));
            }

            var targetPath = Path.Combine(workspacePath, FileName);
            var exists = File.Exists(targetPath);
            return new ProjectInstructionsStatus
            {
                WorkspacePath = workspacePath,
                TargetPath = targetPath,
                Exists = exists,
                CanSave = Directory.Exists(workspacePath),
                ExistingContent = exists ? File.ReadAllText(targetPath, Utf8NoBom) : string.Empty
            };
        }

        public ProjectInstructionsStatus CreateUnsupportedStatus(string workspacePath, string unsupportedReason)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                throw new ArgumentException("Workspace path is required.", nameof(workspacePath));
            }

            return new ProjectInstructionsStatus
            {
                WorkspacePath = workspacePath,
                TargetPath = CombineRuntimePath(workspacePath, FileName),
                Exists = false,
                CanSave = false,
                ExistingContent = string.Empty,
                UnsupportedReason = unsupportedReason ?? string.Empty
            };
        }

        public void SaveLocal(string targetPath, string content)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("Target path is required.", nameof(targetPath));
            }

            var directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(directory ?? targetPath);
            }

            File.WriteAllText(targetPath, NormalizeForSave(content), Utf8NoBom);
        }

        public static string BuildGenerationPrompt()
        {
            return "Please inspect the current repository in read-only mode and generate a complete AGENTS.md for Codex.\n\n" +
                   "Requirements:\n" +
                   "- Read the repository structure, build/test entry points, main projects, existing constraints, and development conventions.\n" +
                   "- Do not run builds, tests, installs, network requests, or write files.\n" +
                   "- Ignore generated and cache directories such as bin, obj, .git, debug, Release, and node_modules.\n" +
                   "- Do not include analysis, progress notes, skill/tool usage notes, or any explanation outside the AGENTS.md itself.\n" +
                   "- The first non-empty line of the response must be the AGENTS.md heading.\n" +
                   "- Output only the complete AGENTS.md content.\n" +
                   "- Make the content specific and actionable; avoid generic guidance.";
        }

        public static string BuildImprovementPrompt(string existingContent)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Please inspect the current repository in read-only mode and generate an improved complete AGENTS.md based on the existing file.");
            builder.AppendLine();
            builder.AppendLine("Requirements:");
            builder.AppendLine("- Preserve existing constraints that are still correct and useful.");
            builder.AppendLine("- Remove stale, duplicate, generic, or process-only content.");
            builder.AppendLine("- Add repository structure, build/test entry points, development conventions, and important cautions.");
            builder.AppendLine("- Do not run builds, tests, installs, network requests, or write files.");
            builder.AppendLine("- Ignore generated and cache directories such as bin, obj, .git, debug, Release, and node_modules.");
            builder.AppendLine("- Do not preserve analysis notes, progress summaries, or instructions about using skills/tools to generate this file.");
            builder.AppendLine("- The first non-empty line of the response must be the AGENTS.md heading.");
            builder.AppendLine("- Output only the complete AGENTS.md content.");
            if (!string.IsNullOrWhiteSpace(existingContent))
            {
                builder.AppendLine();
                builder.AppendLine("Existing AGENTS.md:");
                builder.AppendLine("```markdown");
                builder.AppendLine(existingContent.Trim());
                builder.AppendLine("```");
            }

            return builder.ToString().Trim();
        }

        public static string ExtractGeneratedContent(string modelOutput)
        {
            var text = (modelOutput ?? string.Empty).Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLineEnd = text.IndexOf('\n');
                if (firstLineEnd >= 0)
                {
                    var closingFence = text.LastIndexOf("```", StringComparison.Ordinal);
                    if (closingFence > firstLineEnd)
                    {
                        return text.Substring(firstLineEnd + 1, closingFence - firstLineEnd - 1).Trim();
                    }
                }
            }

            return TrimLeadingNonAgentsContent(text);
        }

        private static string NormalizeForSave(string content)
        {
            return (content ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        }

        private static string TrimLeadingNonAgentsContent(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = (lines[i] ?? string.Empty).Trim();
                if (!line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.IndexOf("AGENTS.md", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (i == 0)
                {
                    return normalized.Trim();
                }

                return string.Join("\n", lines, i, lines.Length - i).Trim();
            }

            return normalized.Trim();
        }

        private static string CombineRuntimePath(string rootPath, string fileName)
        {
            var root = (rootPath ?? string.Empty).TrimEnd('\\', '/');
            var separator = root.IndexOf('/') >= 0 && root.IndexOf('\\') < 0 ? "/" : "\\";
            return root + separator + fileName;
        }
    }
}
