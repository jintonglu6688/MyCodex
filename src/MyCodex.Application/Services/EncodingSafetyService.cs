using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public sealed class EncodingSafetyService
    {
        private const int MaxIssues = 20;
        private const int MaxPreviewLength = 160;
        private const int MaxWholeFileBytes = 1024 * 1024;
        private const int MaxWholeFileLines = 20000;

        private static readonly HashSet<string> TextExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".bat",
                ".cmd",
                ".config",
                ".cs",
                ".cshtml",
                ".csproj",
                ".css",
                ".editorconfig",
                ".gitattributes",
                ".gitignore",
                ".html",
                ".htm",
                ".ini",
                ".js",
                ".json",
                ".jsx",
                ".md",
                ".props",
                ".ps1",
                ".resx",
                ".sh",
                ".sln",
                ".targets",
                ".toml",
                ".ts",
                ".tsx",
                ".txt",
                ".xml",
                ".yaml",
                ".yml"
            };

        private static readonly string[] Latin1MojibakeMarkers =
        {
            "Ã",
            "Â",
            "â€",
            "â€™",
            "â€œ",
            "â€�",
            "â€¦",
            "ä¸",
            "äº",
            "ä»",
            "ä½",
            "å›",
            "æ–",
            "æœ",
            "çš",
            "ï¼",
            "ï½"
        };

        private static readonly string[] GbkMojibakeMarkers =
        {
            "涓",
            "鏂",
            "鍙",
            "璇",
            "鎴",
            "鐨",
            "锛",
            "銆",
            "琛",
            "绋",
            "妗",
            "浠",
            "杩",
            "忚"
        };

        public EncodingSafetyScanResult ScanDiffSnapshot(GitDiffSnapshot snapshot)
        {
            var result = new EncodingSafetyScanResult();
            if (snapshot == null || snapshot.Files.Count == 0)
            {
                return result;
            }

            for (var i = 0; i < snapshot.Files.Count && result.Issues.Count < MaxIssues; i++)
            {
                var entry = snapshot.Files[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath) || !IsTextFileCandidate(entry.FilePath))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(entry.DiffText))
                {
                    ScanDiffEntry(entry, result);
                }
                else
                {
                    ScanCurrentFile(snapshot.WorkspacePath, entry.FilePath, result);
                }
            }

            return result;
        }

        private static void ScanDiffEntry(GitDiffFileEntry entry, EncodingSafetyScanResult result)
        {
            var lines = NormalizeLineEndings(entry.DiffText).Split('\n');
            int? newLineNumber = null;

            for (var index = 0; index < lines.Length && result.Issues.Count < MaxIssues; index++)
            {
                var line = lines[index] ?? string.Empty;
                int parsedNewStart;
                if (TryParseNewHunkStart(line, out parsedNewStart))
                {
                    newLineNumber = parsedNewStart;
                    continue;
                }

                if (line.StartsWith("+++", StringComparison.Ordinal) ||
                    line.StartsWith("---", StringComparison.Ordinal) ||
                    line.StartsWith("\\", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("+", StringComparison.Ordinal))
                {
                    var content = line.Length > 1 ? line.Substring(1) : string.Empty;
                    AddIssueIfSuspicious(result, entry.FilePath, newLineNumber, content);
                    if (newLineNumber.HasValue)
                    {
                        newLineNumber = newLineNumber.Value + 1;
                    }

                    continue;
                }

                if (line.StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }

                if (newLineNumber.HasValue)
                {
                    newLineNumber = newLineNumber.Value + 1;
                }
            }
        }

        private static void ScanCurrentFile(string workspacePath, string filePath, EncodingSafetyScanResult result)
        {
            var absolutePath = ResolveWorkspaceFilePath(workspacePath, filePath);
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                return;
            }

            var info = new FileInfo(absolutePath);
            if (info.Length <= 0 || info.Length > MaxWholeFileBytes)
            {
                return;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(absolutePath);
            }
            catch
            {
                return;
            }

            if (LooksBinary(bytes))
            {
                return;
            }

            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return;
            }

            var lines = NormalizeLineEndings(text).Split('\n');
            var limit = Math.Min(lines.Length, MaxWholeFileLines);
            for (var i = 0; i < limit && result.Issues.Count < MaxIssues; i++)
            {
                AddIssueIfSuspicious(result, filePath, i + 1, lines[i] ?? string.Empty);
            }
        }

        private static void AddIssueIfSuspicious(EncodingSafetyScanResult result, string filePath, int? lineNumber, string text)
        {
            string reason;
            if (!TryGetSuspiciousReason(text, out reason))
            {
                return;
            }

            result.Issues.Add(new EncodingSafetyIssue
            {
                FilePath = filePath,
                LineNumber = lineNumber,
                Reason = reason,
                Preview = BuildPreview(text)
            });
        }

        private static bool TryGetSuspiciousReason(string text, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (text.IndexOf('\uFFFD') >= 0)
            {
                reason = "replacement character";
                return true;
            }

            if (text.IndexOf("锟斤拷", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("ï¿½", StringComparison.Ordinal) >= 0)
            {
                reason = "replacement mojibake";
                return true;
            }

            if (ContainsPrivateUseCharacter(text))
            {
                reason = "private-use mojibake character";
                return true;
            }

            if (LooksLikeUtf8DecodedAsLatin1(text))
            {
                reason = "UTF-8 text appears to be decoded as Latin-1";
                return true;
            }

            if (LooksLikeUtf8DecodedAsGbk(text))
            {
                reason = "UTF-8 text appears to be decoded as GBK";
                return true;
            }

            return false;
        }

        private static bool LooksLikeUtf8DecodedAsLatin1(string text)
        {
            return text.IndexOf("ä¸", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("äº", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("ä»", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("ä½", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("å›", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("æ–", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("æœ", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("çš", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("ï¼", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("ï½", StringComparison.Ordinal) >= 0 ||
                   CountMarkerHits(text, Latin1MojibakeMarkers) >= 3;
        }

        private static bool LooksLikeUtf8DecodedAsGbk(string text)
        {
            return CountMarkerHits(text, GbkMojibakeMarkers) >= 2;
        }

        private static int CountMarkerHits(string text, string[] markers)
        {
            var count = 0;
            for (var i = 0; i < markers.Length; i++)
            {
                if (text.IndexOf(markers[i], StringComparison.Ordinal) >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool ContainsPrivateUseCharacter(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch >= '\uE000' && ch <= '\uF8FF')
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseNewHunkStart(string line, out int newStart)
        {
            newStart = 0;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                return false;
            }

            var plusIndex = line.IndexOf('+');
            if (plusIndex < 0 || plusIndex + 1 >= line.Length)
            {
                return false;
            }

            var index = plusIndex + 1;
            var start = index;
            while (index < line.Length && char.IsDigit(line[index]))
            {
                index++;
            }

            return index > start && int.TryParse(line.Substring(start, index - start), out newStart);
        }

        private static bool IsTextFileCandidate(string filePath)
        {
            var extension = Path.GetExtension(filePath ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(extension) && TextExtensions.Contains(extension))
            {
                return true;
            }

            var fileName = Path.GetFileName(filePath ?? string.Empty);
            return TextExtensions.Contains(fileName);
        }

        private static string ResolveWorkspaceFilePath(string workspacePath, string filePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath) || string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            try
            {
                var root = Path.GetFullPath(workspacePath);
                var candidate = Path.GetFullPath(Path.Combine(root, filePath.Replace('/', Path.DirectorySeparatorChar)));
                var comparison = Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? root
                    : root + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(rootWithSeparator, comparison) &&
                    !string.Equals(candidate, root, comparison))
                {
                    return null;
                }

                return candidate;
            }
            catch
            {
                return null;
            }
        }

        private static bool LooksBinary(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            var limit = Math.Min(bytes.Length, 4096);
            for (var i = 0; i < limit; i++)
            {
                if (bytes[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildPreview(string text)
        {
            var preview = (text ?? string.Empty).Trim();
            if (preview.Length <= MaxPreviewLength)
            {
                return preview;
            }

            return preview.Substring(0, MaxPreviewLength) + "...";
        }

        private static string NormalizeLineEndings(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
