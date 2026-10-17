using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class CodexThreadHistoryRepairService : IThreadHistoryRepairService
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly List<string> _allowedRoots;
        private readonly Action<string, string, string> _replaceFile;

        public CodexThreadHistoryRepairService()
            : this(new string[0])
        {
        }

        public CodexThreadHistoryRepairService(IEnumerable<string> allowedRoots)
            : this(allowedRoots, File.Replace)
        {
        }

        internal CodexThreadHistoryRepairService(
            IEnumerable<string> allowedRoots,
            Action<string, string, string> replaceFile)
        {
            _replaceFile = replaceFile ?? throw new ArgumentNullException(nameof(replaceFile));
            _allowedRoots = new List<string>();
            foreach (var root in allowedRoots ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                try
                {
                    _allowedRoots.Add(AppServerPathUtilities.NormalizeLocalPath(root));
                }
                catch
                {
                }
            }
        }

        public Task<ThreadHistoryRepairResult> TryRepairInvalidEncryptedContentAsync(ChatSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.SessionPath))
            {
                return Task.FromResult(ThreadHistoryRepairResult.NotRepaired("session path missing"));
            }

            if (!TryNormalizeRepairTarget(session.SessionPath, out var sessionPath))
            {
                return Task.FromResult(ThreadHistoryRepairResult.NotRepaired("unsupported session path"));
            }

            if (!File.Exists(sessionPath))
            {
                return Task.FromResult(ThreadHistoryRepairResult.NotRepaired("session file missing"));
            }

            var originalText = File.ReadAllText(sessionPath, Encoding.UTF8);
            var hasTrailingNewline = EndsWithNewline(originalText);
            var lineEnding = originalText.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var lines = SplitLines(originalText);
            if (lines.Count == 0)
            {
                return Task.FromResult(ThreadHistoryRepairResult.NotRepaired("session file empty"));
            }

            var repairCount = 0;
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) ||
                    line.IndexOf("encrypted", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string repairedLine;
                var lineRepairCount = RepairLine(line, out repairedLine);
                if (lineRepairCount <= 0)
                {
                    continue;
                }

                lines[i] = repairedLine;
                repairCount += lineRepairCount;
            }

            if (repairCount <= 0)
            {
                return Task.FromResult(ThreadHistoryRepairResult.NotRepaired("no corrupted encrypted content found"));
            }

            var repairedText = string.Join(lineEnding, lines.ToArray());
            if (hasTrailingNewline)
            {
                repairedText += lineEnding;
            }

            var temporaryPath = sessionPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var backupPath = sessionPath + "." + Guid.NewGuid().ToString("N") + ".bak";
            try
            {
                File.WriteAllText(temporaryPath, repairedText, Utf8NoBom);
                VerifyJsonLines(temporaryPath);

                if (!TryNormalizeRepairTarget(sessionPath, out sessionPath))
                {
                    return Task.FromResult(ThreadHistoryRepairResult.NotRepaired("unsupported session path"));
                }

                if (File.Exists(sessionPath))
                {
                    _replaceFile(temporaryPath, sessionPath, backupPath);
                }
                else
                {
                    File.Move(temporaryPath, sessionPath);
                }

                return Task.FromResult(ThreadHistoryRepairResult.CreateRepaired(repairCount));
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is InvalidDataException ||
                ex is UnauthorizedAccessException ||
                ex is InvalidOperationException ||
                ex is ArgumentException ||
                ex is NotSupportedException)
            {
                return Task.FromResult(ThreadHistoryRepairResult.NotRepaired("repair write failed"));
            }
            finally
            {
                TryDeleteTaskOwnedTemporaryFile(temporaryPath);
            }
        }

        private bool TryNormalizeRepairTarget(string sessionPath, out string normalizedPath)
        {
            normalizedPath = null;
            if (string.IsNullOrWhiteSpace(sessionPath) ||
                AppServerPathUtilities.LooksLikeUnixAbsolutePath(sessionPath))
            {
                return false;
            }

            try
            {
                normalizedPath = CodexSessionPathPolicy.ValidateDeletePath(sessionPath, _allowedRoots);
                return string.Equals(Path.GetExtension(normalizedPath), ".jsonl", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void VerifyJsonLines(string path)
        {
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (AppServerJsonValue.DeserializeDictionary(line) == null)
                {
                    throw new InvalidDataException("Repaired session history contains an invalid JSON line.");
                }
            }
        }

        private static void TryDeleteTaskOwnedTemporaryFile(string temporaryPath)
        {
            if (string.IsNullOrWhiteSpace(temporaryPath))
            {
                return;
            }

            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
            }
        }

        private static int RepairLine(string line, out string repairedLine)
        {
            repairedLine = line;
            if (string.IsNullOrWhiteSpace(line))
            {
                return 0;
            }

            var repairCount = 0;
            var searchIndex = 0;
            while (TryFindEncryptedContentProperty(repairedLine, searchIndex, out var property))
            {
                repairedLine = RemoveJsonProperty(repairedLine, property);
                searchIndex = Math.Max(0, property.PropertyStartIndex);
                repairCount++;
            }

            return repairCount;
        }

        private static bool TryFindEncryptedContentProperty(string line, int startIndex, out JsonStringProperty property)
        {
            property = null;
            var encryptedContentIndex = line.IndexOf("\"encrypted_content\"", startIndex, StringComparison.OrdinalIgnoreCase);
            var encryptedContentCamelIndex = line.IndexOf("\"encryptedContent\"", startIndex, StringComparison.OrdinalIgnoreCase);
            var propertyStartIndex = ChooseEarliestIndex(encryptedContentIndex, encryptedContentCamelIndex);
            if (propertyStartIndex < 0)
            {
                return false;
            }

            var keyEndIndex = propertyStartIndex +
                (propertyStartIndex == encryptedContentIndex ? "\"encrypted_content\"".Length : "\"encryptedContent\"".Length);
            var colonIndex = SkipWhitespace(line, keyEndIndex);
            if (colonIndex >= line.Length || line[colonIndex] != ':')
            {
                return TryFindEncryptedContentProperty(line, keyEndIndex, out property);
            }

            var valueStartIndex = SkipWhitespace(line, colonIndex + 1);
            if (valueStartIndex >= line.Length || line[valueStartIndex] != '"')
            {
                return TryFindEncryptedContentProperty(line, valueStartIndex, out property);
            }

            var valueEndIndex = FindJsonStringEnd(line, valueStartIndex);
            if (valueEndIndex < 0)
            {
                return false;
            }

            property = new JsonStringProperty
            {
                PropertyStartIndex = propertyStartIndex,
                ValueEndIndex = valueEndIndex,
                RawValue = line.Substring(valueStartIndex + 1, valueEndIndex - valueStartIndex - 1)
            };
            return true;
        }

        private static string RemoveJsonProperty(string line, JsonStringProperty property)
        {
            var removeStartIndex = property.PropertyStartIndex;
            var removeEndIndex = property.ValueEndIndex + 1;
            var nextIndex = SkipWhitespace(line, removeEndIndex);
            if (nextIndex < line.Length && line[nextIndex] == ',')
            {
                removeEndIndex = nextIndex + 1;
            }
            else
            {
                var previousIndex = PreviousNonWhitespace(line, removeStartIndex - 1);
                if (previousIndex >= 0 && line[previousIndex] == ',')
                {
                    removeStartIndex = previousIndex;
                }
            }

            return line.Remove(removeStartIndex, removeEndIndex - removeStartIndex);
        }

        private static int ChooseEarliestIndex(int left, int right)
        {
            if (left < 0)
            {
                return right;
            }

            if (right < 0)
            {
                return left;
            }

            return Math.Min(left, right);
        }

        private static int SkipWhitespace(string value, int startIndex)
        {
            var index = Math.Max(0, startIndex);
            while (index < value.Length && char.IsWhiteSpace(value[index]))
            {
                index++;
            }

            return index;
        }

        private static int PreviousNonWhitespace(string value, int startIndex)
        {
            var index = Math.Min(value.Length - 1, startIndex);
            while (index >= 0 && char.IsWhiteSpace(value[index]))
            {
                index--;
            }

            return index;
        }

        private static int FindJsonStringEnd(string value, int valueStartIndex)
        {
            var escaped = false;
            for (var i = valueStartIndex + 1; i < value.Length; i++)
            {
                var ch = value[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool EndsWithNewline(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   (value.EndsWith("\n", StringComparison.Ordinal) ||
                    value.EndsWith("\r\n", StringComparison.Ordinal));
        }

        private static List<string> SplitLines(string value)
        {
            var normalized = (value ?? string.Empty).Replace("\r\n", "\n");
            var parts = new List<string>(normalized.Split(new[] { '\n' }));
            if (parts.Count > 0 && parts[parts.Count - 1].Length == 0 && EndsWithNewline(value))
            {
                parts.RemoveAt(parts.Count - 1);
            }

            return parts;
        }

        private sealed class JsonStringProperty
        {
            public int PropertyStartIndex { get; set; }

            public int ValueEndIndex { get; set; }

            public string RawValue { get; set; }
        }
    }
}
