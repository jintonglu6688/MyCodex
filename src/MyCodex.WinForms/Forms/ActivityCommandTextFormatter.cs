using System;

namespace MyCodex.WinForms.Forms
{
    internal static class ActivityCommandTextFormatter
    {
        internal static string CompactCompletionText(
            string completionText,
            string commandText,
            string workingDirectory)
        {
            var normalizedCompletion = completionText ?? string.Empty;
            var normalizedCommand = (commandText ?? string.Empty).Trim();
            if (normalizedCommand.Length == 0)
            {
                return CompactWorkingDirectory(normalizedCompletion, workingDirectory);
            }

            var compactCommand = CompactCommandText(normalizedCommand, workingDirectory);
            var commandIndex = normalizedCompletion.IndexOf(normalizedCommand, StringComparison.Ordinal);
            if (commandIndex >= 0 && !string.Equals(compactCommand, normalizedCommand, StringComparison.Ordinal))
            {
                normalizedCompletion = normalizedCompletion.Substring(0, commandIndex) +
                    compactCommand +
                    normalizedCompletion.Substring(commandIndex + normalizedCommand.Length);
            }

            return CompactWorkingDirectory(normalizedCompletion, workingDirectory);
        }

        internal static string CompactCommandText(string commandText, string workingDirectory)
        {
            var normalized = (commandText ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            normalized = CompactWorkingDirectory(
                RemoveWorkingDirectoryPrompt(normalized, workingDirectory),
                workingDirectory);

            LeadingExecutable executable;
            if (!TryReadLeadingExecutable(normalized, out executable))
            {
                return CompactWorkingDirectory(normalized, workingDirectory);
            }

            var fileName = GetFileName(executable.Path);
            var shellPayload = TryExtractShellPayload(normalized, executable.EndIndex, fileName);
            if (!string.IsNullOrWhiteSpace(shellPayload))
            {
                return CompactWorkingDirectory(shellPayload, workingDirectory);
            }

            if (!LooksLikeAbsolutePath(executable.Path) || string.IsNullOrWhiteSpace(fileName))
            {
                return CompactWorkingDirectory(normalized, workingDirectory);
            }

            var compactExecutable = executable.Quote == '\0'
                ? fileName
                : executable.Quote + fileName + executable.Quote;
            var compact = normalized.Substring(0, executable.StartIndex) +
                compactExecutable +
                normalized.Substring(executable.EndIndex);
            return CompactWorkingDirectory(compact, workingDirectory);
        }

        private static string TryExtractShellPayload(string command, int executableEndIndex, string executableName)
        {
            var normalizedName = (executableName ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedName == "powershell.exe" ||
                normalizedName == "powershell" ||
                normalizedName == "pwsh.exe" ||
                normalizedName == "pwsh")
            {
                return ExtractArgumentValue(command, executableEndIndex, "-Command");
            }

            if (normalizedName == "cmd.exe" || normalizedName == "cmd")
            {
                return ExtractArgumentValue(command, executableEndIndex, "/c");
            }

            if (normalizedName == "bash" ||
                normalizedName == "bash.exe" ||
                normalizedName == "sh" ||
                normalizedName == "sh.exe" ||
                normalizedName == "zsh")
            {
                var payload = ExtractArgumentValue(command, executableEndIndex, "-lc");
                return string.IsNullOrWhiteSpace(payload)
                    ? ExtractArgumentValue(command, executableEndIndex, "-c")
                    : payload;
            }

            return null;
        }

        private static string ExtractArgumentValue(string command, int startIndex, string argumentName)
        {
            var searchIndex = Math.Max(0, Math.Min(startIndex, command.Length));
            while (searchIndex < command.Length)
            {
                var argumentIndex = command.IndexOf(argumentName, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (argumentIndex < 0)
                {
                    return null;
                }

                var hasBoundaryBefore = argumentIndex == 0 || char.IsWhiteSpace(command[argumentIndex - 1]);
                var valueStart = argumentIndex + argumentName.Length;
                var hasBoundaryAfter = valueStart >= command.Length || char.IsWhiteSpace(command[valueStart]);
                if (hasBoundaryBefore && hasBoundaryAfter)
                {
                    var value = command.Substring(valueStart).Trim();
                    return TrimMatchingOuterQuotes(value);
                }

                searchIndex = valueStart;
            }

            return null;
        }

        private static string TrimMatchingOuterQuotes(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length < 2)
            {
                return normalized;
            }

            var first = normalized[0];
            var last = normalized[normalized.Length - 1];
            return (first == '"' || first == '\'') && last == first
                ? normalized.Substring(1, normalized.Length - 2)
                : normalized;
        }

        private static string RemoveWorkingDirectoryPrompt(string command, string workingDirectory)
        {
            var normalizedDirectory = TrimTrailingSeparators(workingDirectory);
            if (normalizedDirectory.Length == 0 ||
                !command.StartsWith(normalizedDirectory, GetPathComparison(normalizedDirectory)))
            {
                return command;
            }

            var suffix = command.Substring(normalizedDirectory.Length);
            if (!suffix.StartsWith(">", StringComparison.Ordinal))
            {
                return command;
            }

            return suffix.Substring(1).TrimStart();
        }

        private static string CompactWorkingDirectory(string text, string workingDirectory)
        {
            var normalizedDirectory = TrimTrailingSeparators(workingDirectory);
            if (string.IsNullOrEmpty(text) || normalizedDirectory.Length == 0)
            {
                return text ?? string.Empty;
            }

            var comparison = GetPathComparison(normalizedDirectory);
            var result = text;
            var searchIndex = 0;
            while (searchIndex < result.Length)
            {
                var matchIndex = result.IndexOf(normalizedDirectory, searchIndex, comparison);
                if (matchIndex < 0)
                {
                    break;
                }

                var matchEndIndex = matchIndex + normalizedDirectory.Length;
                if (matchEndIndex < result.Length &&
                    !IsPathBoundary(result[matchEndIndex]))
                {
                    searchIndex = matchEndIndex;
                    continue;
                }

                result = result.Substring(0, matchIndex) + "." + result.Substring(matchEndIndex);
                searchIndex = matchIndex + 1;
            }

            return result;
        }

        private static bool IsPathBoundary(char value)
        {
            return value == '\\' ||
                   value == '/' ||
                   value == '>' ||
                   value == '"' ||
                   value == '\'' ||
                   char.IsWhiteSpace(value);
        }

        private static bool TryReadLeadingExecutable(string command, out LeadingExecutable executable)
        {
            executable = null;
            var index = 0;
            while (index < command.Length && char.IsWhiteSpace(command[index]))
            {
                index++;
            }

            if (index < command.Length && command[index] == '&')
            {
                index++;
                while (index < command.Length && char.IsWhiteSpace(command[index]))
                {
                    index++;
                }
            }

            if (index >= command.Length)
            {
                return false;
            }

            var startIndex = index;
            var quote = command[index] == '"' || command[index] == '\'' ? command[index] : '\0';
            if (quote != '\0')
            {
                index++;
                var pathStart = index;
                while (index < command.Length && command[index] != quote)
                {
                    index++;
                }

                if (index >= command.Length)
                {
                    return false;
                }

                executable = new LeadingExecutable(
                    startIndex,
                    index + 1,
                    command.Substring(pathStart, index - pathStart),
                    quote);
                return true;
            }

            while (index < command.Length && !char.IsWhiteSpace(command[index]))
            {
                index++;
            }

            executable = new LeadingExecutable(
                startIndex,
                index,
                command.Substring(startIndex, index - startIndex),
                '\0');
            return true;
        }

        private static string GetFileName(string path)
        {
            var normalized = (path ?? string.Empty).TrimEnd('/', '\\');
            var separatorIndex = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf('\\'));
            return separatorIndex >= 0 ? normalized.Substring(separatorIndex + 1) : normalized;
        }

        private static bool LooksLikeAbsolutePath(string path)
        {
            var normalized = path ?? string.Empty;
            return normalized.StartsWith("/", StringComparison.Ordinal) ||
                   normalized.StartsWith("\\\\", StringComparison.Ordinal) ||
                   normalized.Length >= 3 &&
                   char.IsLetter(normalized[0]) &&
                   normalized[1] == ':' &&
                   (normalized[2] == '\\' || normalized[2] == '/');
        }

        private static string TrimTrailingSeparators(string path)
        {
            return (path ?? string.Empty).Trim().TrimEnd('/', '\\');
        }

        private static StringComparison GetPathComparison(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   path.Length >= 2 &&
                   path[1] == ':'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        private sealed class LeadingExecutable
        {
            internal LeadingExecutable(int startIndex, int endIndex, string path, char quote)
            {
                StartIndex = startIndex;
                EndIndex = endIndex;
                Path = path ?? string.Empty;
                Quote = quote;
            }

            internal int StartIndex { get; }

            internal int EndIndex { get; }

            internal string Path { get; }

            internal char Quote { get; }
        }
    }
}
