using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public sealed class GitDiffService
    {
        private readonly WorkspaceService _workspaceService = new WorkspaceService();

        public GitDiffSnapshot BuildSnapshotFromTurnDiff(
            string workspacePath,
            string diffText,
            IReadOnlyList<string> changedFiles)
        {
            var snapshot = new GitDiffSnapshot
            {
                WorkspacePath = string.IsNullOrWhiteSpace(workspacePath)
                    ? null
                    : _workspaceService.NormalizeWorkspacePath(workspacePath)
            };

            var parsedEntries = ParseUnifiedDiffEntries(diffText);
            for (var i = 0; i < parsedEntries.Count; i++)
            {
                snapshot.Files.Add(parsedEntries[i]);
            }

            if (changedFiles != null)
            {
                for (var i = 0; i < changedFiles.Count; i++)
                {
                    var changedFile = NormalizePathToken(changedFiles[i]);
                    if (string.IsNullOrWhiteSpace(changedFile))
                    {
                        continue;
                    }

                    if (snapshot.Files.Any(item => string.Equals(item.FilePath, changedFile, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    snapshot.Files.Add(new GitDiffFileEntry
                    {
                        FilePath = changedFile,
                        ChangeKind = "modified",
                        DiffText = string.Empty
                    });
                }
            }

            return snapshot;
        }

        private static string NormalizePathToken(string value)
        {
            return NormalizeGitFilePath(value, true);
        }

        private static string NormalizeGitFilePath(string value, bool stripDiffPrefix)
        {
            var normalized = UnquoteGitPathToken((value ?? string.Empty).Trim());
            if (stripDiffPrefix &&
                normalized.Length > 2 &&
                (normalized.StartsWith("a/", StringComparison.OrdinalIgnoreCase) ||
                 normalized.StartsWith("b/", StringComparison.OrdinalIgnoreCase)))
            {
                normalized = normalized.Substring(2);
            }

            return normalized.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string UnquoteGitPathToken(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length >= 2 &&
                normalized[0] == '"' &&
                normalized[normalized.Length - 1] == '"')
            {
                return DecodeGitQuotedPath(normalized.Substring(1, normalized.Length - 2));
            }

            return normalized;
        }

        private static string DecodeGitQuotedPath(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var bytes = new List<byte>();
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (current == '\\' && i + 1 < value.Length)
                {
                    var next = value[i + 1];
                    switch (next)
                    {
                        case '\\':
                            bytes.Add((byte)'\\');
                            i++;
                            continue;
                        case '"':
                            bytes.Add((byte)'"');
                            i++;
                            continue;
                        case 'a':
                            bytes.Add((byte)'\a');
                            i++;
                            continue;
                        case 'b':
                            bytes.Add((byte)'\b');
                            i++;
                            continue;
                        case 'f':
                            bytes.Add((byte)'\f');
                            i++;
                            continue;
                        case 'n':
                            bytes.Add((byte)'\n');
                            i++;
                            continue;
                        case 'r':
                            bytes.Add((byte)'\r');
                            i++;
                            continue;
                        case 't':
                            bytes.Add((byte)'\t');
                            i++;
                            continue;
                        case 'v':
                            bytes.Add((byte)'\v');
                            i++;
                            continue;
                        default:
                            if (IsOctalDigit(next))
                            {
                                var octalValue = next - '0';
                                var digitCount = 1;
                                while (digitCount < 3 &&
                                       i + 1 + digitCount < value.Length &&
                                       IsOctalDigit(value[i + 1 + digitCount]))
                                {
                                    octalValue = (octalValue * 8) + (value[i + 1 + digitCount] - '0');
                                    digitCount++;
                                }

                                bytes.Add((byte)octalValue);
                                i += digitCount;
                                continue;
                            }

                            bytes.AddRange(Encoding.UTF8.GetBytes(next.ToString()));
                            i++;
                            continue;
                    }
                }

                bytes.AddRange(Encoding.UTF8.GetBytes(current.ToString()));
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private static bool IsOctalDigit(char value)
        {
            return value >= '0' && value <= '7';
        }

        private static void PopulateDiffLineCounts(GitDiffFileEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            var counts = UnifiedDiffLineCounter.Count(entry.DiffText);
            entry.AddedLineCount = counts.Additions;
            entry.DeletedLineCount = counts.Deletions;
        }

        private static List<GitDiffFileEntry> ParseUnifiedDiffEntries(string diffText)
        {
            var result = new List<GitDiffFileEntry>();
            if (string.IsNullOrWhiteSpace(diffText))
            {
                return result;
            }

            var lines = diffText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            GitDiffFileEntry currentEntry = null;
            StringBuilder currentDiff = null;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i] ?? string.Empty;
                if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    FinalizeDiffEntry(result, currentEntry, currentDiff);

                    currentEntry = CreateDiffEntryFromHeader(line);
                    currentDiff = new StringBuilder();
                    currentDiff.AppendLine(line);
                    continue;
                }

                if (currentEntry == null || currentDiff == null)
                {
                    continue;
                }

                currentDiff.AppendLine(line);

                if (line.StartsWith("new file mode ", StringComparison.Ordinal))
                {
                    currentEntry.ChangeKind = "added";
                }
                else if (line.StartsWith("deleted file mode ", StringComparison.Ordinal))
                {
                    currentEntry.ChangeKind = "deleted";
                }
                else if (line.StartsWith("rename from ", StringComparison.Ordinal))
                {
                    currentEntry.ChangeKind = "renamed";
                    currentEntry.OriginalFilePath = NormalizePathToken(line.Substring("rename from ".Length));
                }
                else if (line.StartsWith("rename to ", StringComparison.Ordinal))
                {
                    currentEntry.FilePath = NormalizePathToken(line.Substring("rename to ".Length));
                }
            }

            FinalizeDiffEntry(result, currentEntry, currentDiff);
            return result;
        }

        private static GitDiffFileEntry CreateDiffEntryFromHeader(string headerLine)
        {
            var entry = new GitDiffFileEntry
            {
                ChangeKind = "modified"
            };

            const string prefix = "diff --git ";
            var payload = headerLine ?? string.Empty;
            if (!payload.StartsWith(prefix, StringComparison.Ordinal))
            {
                return entry;
            }

            var index = prefix.Length;
            string oldPathToken;
            string newPathToken;
            if (TryReadGitPathToken(payload, ref index, out oldPathToken) &&
                TryReadGitPathToken(payload, ref index, out newPathToken))
            {
                var oldPath = NormalizePathToken(oldPathToken);
                var newPath = NormalizePathToken(newPathToken);
                entry.OriginalFilePath = IsDevNullPath(oldPathToken) ? null : oldPath;
                entry.FilePath = IsDevNullPath(newPathToken)
                    ? oldPath
                    : newPath;
            }

            return entry;
        }

        private static bool TryReadGitPathToken(string text, ref int index, out string token)
        {
            token = string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            while (index < text.Length && text[index] == ' ')
            {
                index++;
            }

            if (index >= text.Length)
            {
                return false;
            }

            if (text[index] == '"')
            {
                var builder = new StringBuilder();
                builder.Append('"');
                index++;

                while (index < text.Length)
                {
                    var current = text[index];
                    if (current == '"')
                    {
                        builder.Append('"');
                        index++;
                        token = builder.ToString();
                        return true;
                    }

                    if (current == '\\' && index + 1 < text.Length)
                    {
                        builder.Append(current);
                        builder.Append(text[index + 1]);
                        index += 2;
                        continue;
                    }

                    builder.Append(current);
                    index++;
                }

                return false;
            }

            var start = index;
            while (index < text.Length && text[index] != ' ')
            {
                index++;
            }

            token = text.Substring(start, index - start);
            return token.Length > 0;
        }

        private static bool IsDevNullPath(string value)
        {
            return string.Equals(UnquoteGitPathToken((value ?? string.Empty).Trim()), "/dev/null", StringComparison.OrdinalIgnoreCase);
        }

        private static void FinalizeDiffEntry(
            List<GitDiffFileEntry> result,
            GitDiffFileEntry entry,
            StringBuilder diffBuilder)
        {
            if (entry == null || diffBuilder == null)
            {
                return;
            }

            entry.DiffText = diffBuilder.ToString().Trim();
            PopulateDiffLineCounts(entry);
            if (!string.IsNullOrWhiteSpace(entry.FilePath))
            {
                result.Add(entry);
            }
        }
    }
}
