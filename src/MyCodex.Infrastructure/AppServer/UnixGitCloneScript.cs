using System;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class UnixGitCloneScript
    {
        public const string CloneScript =
            "set -u\n" +
            "repository=${1:-}\n" +
            "parent=${2:-}\n" +
            "folder=${3:-}\n" +
            "submodules=${4:-false}\n" +
            "if [ -z \"$repository\" ]; then exit 64; fi\n" +
            "if [ -z \"$parent\" ]; then exit 64; fi\n" +
            "if [ -z \"$folder\" ]; then exit 64; fi\n" +
            "case \"$parent\" in '~') parent=\"$HOME\" ;; '~/'*) parent=\"$HOME/${parent#~/}\" ;; esac\n" +
            "mkdir -p \"$parent\"\n" +
            "if [ -d \"$parent/$folder\" ] && [ \"$(find \"$parent/$folder\" -mindepth 1 -maxdepth 1 2>/dev/null | head -n 1)\" ]; then echo 'The target folder already exists and is not empty.' >&2; exit 73; fi\n" +
            "if [ \"$submodules\" = true ]; then git -C \"$parent\" clone --progress --recurse-submodules \"$repository\" \"$folder\"; else git -C \"$parent\" clone --progress \"$repository\" \"$folder\"; fi\n" +
            "exit_code=$?\n" +
            "if [ \"$exit_code\" -eq 0 ]; then printf '\\nMYCODEX_CLONE_TARGET\\t%s/%s\\n' \"$parent\" \"$folder\"; fi\n" +
            "exit \"$exit_code\"";

        public static string NormalizeRequired(string value, string argumentName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(argumentName + " cannot be empty.", argumentName);
            }

            return value.Trim();
        }

        public static void ValidateFolderName(string folderName)
        {
            if (string.Equals(folderName, ".", StringComparison.Ordinal) ||
                string.Equals(folderName, "..", StringComparison.Ordinal) ||
                folderName.IndexOfAny(new[] { '/', '\\', '\r', '\n', '\t' }) >= 0)
            {
                throw new ArgumentException(SharedLocalization.Text("GitCloneService.Error.InvalidTargetFolder", "Target folder name contains invalid characters."));
            }
        }

        public static string CombineUnixPath(string parentDirectory, string folderName)
        {
            var parent = (parentDirectory ?? string.Empty).TrimEnd('/');
            return parent.Length == 0 ? folderName : parent + "/" + folderName;
        }

        public static string ExtractCloneTargetPath(string output, out string targetPath)
        {
            targetPath = null;
            if (string.IsNullOrWhiteSpace(output))
            {
                return output ?? string.Empty;
            }

            var builder = new StringBuilder();
            var lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("MYCODEX_CLONE_TARGET\t", StringComparison.Ordinal))
                {
                    targetPath = line.Substring("MYCODEX_CLONE_TARGET\t".Length).Trim();
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(line);
            }

            return builder.ToString().TrimEnd();
        }

        public static void ReportBufferedProgress(IProgress<GitCloneProgress> progress, string text, bool isError)
        {
            if (progress == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0)
                {
                    continue;
                }

                progress.Report(new GitCloneProgress
                {
                    Text = GitOutputSanitizer.RedactSensitiveText(lines[i]),
                    IsError = isError
                });
            }
        }
    }
}
