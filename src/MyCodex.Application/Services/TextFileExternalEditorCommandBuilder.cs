using System;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;

namespace MyCodex.Application.Services
{
    public static class TextFileExternalEditorCommandBuilder
    {
        public static TextFileExternalEditorCommand Build(
            TextFileOpenPreference preference,
            TextFileOpenRequest request)
        {
            preference = preference ?? new TextFileOpenPreference();
            request = request ?? new TextFileOpenRequest();
            var mode = TextFileOpenMode.Normalize(preference.Mode);
            var filePath = request.FilePath ?? string.Empty;

            if (string.Equals(mode, TextFileOpenMode.VsCode, StringComparison.OrdinalIgnoreCase))
            {
                return new TextFileExternalEditorCommand
                {
                    ExecutablePath = ResolveExecutable(preference.ExternalEditorPath, "code"),
                    Arguments = BuildVsCodeArguments(filePath, request.Line, request.Column),
                    UseShellExecute = false
                };
            }

            if (string.Equals(mode, TextFileOpenMode.NotepadPlusPlus, StringComparison.OrdinalIgnoreCase))
            {
                return new TextFileExternalEditorCommand
                {
                    ExecutablePath = ResolveExecutable(preference.ExternalEditorPath, "notepad++"),
                    Arguments = BuildNotepadPlusPlusArguments(filePath, request.Line, request.Column),
                    UseShellExecute = false
                };
            }

            if (string.Equals(mode, TextFileOpenMode.Custom, StringComparison.OrdinalIgnoreCase))
            {
                return new TextFileExternalEditorCommand
                {
                    ExecutablePath = preference.ExternalEditorPath ?? string.Empty,
                    Arguments = BuildCustomArguments(preference.ExternalEditorArguments, filePath, request.Line, request.Column),
                    UseShellExecute = false
                };
            }

            return new TextFileExternalEditorCommand
            {
                ExecutablePath = filePath,
                Arguments = string.Empty,
                UseShellExecute = true
            };
        }

        private static string ResolveExecutable(string configuredPath, string fallback)
        {
            return string.IsNullOrWhiteSpace(configuredPath) ? fallback : configuredPath.Trim();
        }

        private static string BuildVsCodeArguments(string filePath, int? line, int? column)
        {
            if (line.HasValue && line.Value > 0)
            {
                var target = filePath + ":" + line.Value + ":" + Math.Max(1, column.GetValueOrDefault(1));
                return "-g " + QuoteFilePath(target);
            }

            return QuoteFilePath(filePath);
        }

        private static string BuildNotepadPlusPlusArguments(string filePath, int? line, int? column)
        {
            var prefix = string.Empty;
            if (line.HasValue && line.Value > 0)
            {
                prefix += "-n" + line.Value + " ";
            }

            if (column.HasValue && column.Value > 0)
            {
                prefix += "-c" + column.Value + " ";
            }

            return prefix + QuoteFilePath(filePath);
        }

        private static string BuildCustomArguments(string template, string filePath, int? line, int? column)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return QuoteFilePath(filePath);
            }

            return template
                .Replace("{file}", QuoteFilePath(filePath))
                .Replace("{line}", line.HasValue && line.Value > 0 ? line.Value.ToString() : string.Empty)
                .Replace("{column}", column.HasValue && column.Value > 0 ? column.Value.ToString() : string.Empty);
        }

        private static string QuoteFilePath(string filePath)
        {
            var quoted = WindowsCommandLineArgumentEscaper.Quote(filePath);
            if (quoted.Length >= 2 && quoted[0] == '"' && quoted[quoted.Length - 1] == '"')
            {
                return quoted;
            }

            return "\"" + quoted + "\"";
        }
    }
}
