using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MyCodex.WinForms.Forms
{
    internal static class ChatMessageTextSanitizer
    {
        private static readonly Regex LinkRegex = new Regex(@"\[([^\]\r\n]+)\]\(([^\)\r\n]+)\)", RegexOptions.Compiled);
        private static readonly Regex BoldRegex = new Regex(@"(\*\*|__)(.+?)\1", RegexOptions.Compiled);
        private static readonly Regex ItalicRegex = new Regex(@"(?<!\w)(\*|_)([^\*_]+?)\1(?!\w)", RegexOptions.Compiled);
        private static readonly Regex InlineCodeRegex = new Regex(@"`([^`\r\n]+)`", RegexOptions.Compiled);
        private static readonly Regex StrikeRegex = new Regex(@"~~(.+?)~~", RegexOptions.Compiled);
        private static readonly Regex HeadingRegex = new Regex(@"^(\s*(?:[^:\r\n]{1,40}:\s*)?)#{1,6}\s+", RegexOptions.Compiled);
        private static readonly Regex BlockQuoteRegex = new Regex(@"^\s{0,3}>\s?", RegexOptions.Compiled);
        private static readonly Regex UnorderedListRegex = new Regex(@"^\s*[-*+]\s+", RegexOptions.Compiled);
        private static readonly Regex OrderedListRegex = new Regex(@"^\s*\d+[\.)]\s+", RegexOptions.Compiled);
        private static readonly Regex TableSeparatorRegex = new Regex(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$", RegexOptions.Compiled);
        private static readonly Regex HorizontalRuleRegex = new Regex(@"^\s*([-*_])\s*(\1\s*){2,}$", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        internal static bool ShouldSanitizeSender(string sender)
        {
            return string.Equals(sender, "codex", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sender, "Codex", StringComparison.OrdinalIgnoreCase);
        }

        internal static string SanitizeForDisplay(string text)
        {
            return CollapseWhitespace(string.Join(" ", SanitizeForReadableLines(text)));
        }

        internal static string SanitizeForReadableText(string text)
        {
            return string.Join(Environment.NewLine, SanitizeForReadableLines(text)).Trim();
        }

        internal static string[] SanitizeForReadableLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<string>();
            }

            var normalized = text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            var lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
            var result = new List<string>();
            var insideFence = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine ?? string.Empty;
                var trimmed = line.Trim();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
                    trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    insideFence = !insideFence;
                    continue;
                }

                if (!insideFence && (TableSeparatorRegex.IsMatch(line) || HorizontalRuleRegex.IsMatch(line)))
                {
                    continue;
                }

                var cleaned = SanitizeLine(line, !insideFence);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    result.Add(cleaned);
                }
            }

            return result.ToArray();
        }

        private static string SanitizeLine(string line, bool removeMarkdownLineMarkers)
        {
            var cleaned = RemoveEmoji(line ?? string.Empty);
            if (removeMarkdownLineMarkers)
            {
                cleaned = BlockQuoteRegex.Replace(cleaned, string.Empty);
                cleaned = HeadingRegex.Replace(cleaned, "$1");
                cleaned = UnorderedListRegex.Replace(cleaned, string.Empty);
                cleaned = OrderedListRegex.Replace(cleaned, string.Empty);
            }

            cleaned = LinkRegex.Replace(cleaned, "$1");
            cleaned = InlineCodeRegex.Replace(cleaned, "$1");
            cleaned = BoldRegex.Replace(cleaned, "$2");
            cleaned = ItalicRegex.Replace(cleaned, "$2");
            cleaned = StrikeRegex.Replace(cleaned, "$1");
            cleaned = cleaned.Replace("`", string.Empty);
            return CollapseWhitespace(cleaned);
        }

        private static string CollapseWhitespace(string text)
        {
            return WhitespaceRegex.Replace(text ?? string.Empty, " ").Trim();
        }

        private static string RemoveEmoji(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(text.Length);
            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                int codePoint;
                if (char.IsHighSurrogate(current))
                {
                    if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                    {
                        continue;
                    }

                    codePoint = char.ConvertToUtf32(current, text[index + 1]);
                    index++;
                }
                else if (char.IsLowSurrogate(current))
                {
                    continue;
                }
                else
                {
                    codePoint = current;
                }

                if (IsEmojiCodePoint(codePoint))
                {
                    continue;
                }

                builder.Append(char.ConvertFromUtf32(codePoint));
            }

            return builder.ToString();
        }

        private static bool IsEmojiCodePoint(int codePoint)
        {
            return codePoint == 0x200D ||
                   codePoint == 0x20E3 ||
                   (codePoint >= 0xFE00 && codePoint <= 0xFE0F) ||
                   (codePoint >= 0x1F000 && codePoint <= 0x1FAFF) ||
                   (codePoint >= 0x2600 && codePoint <= 0x27BF);
        }
    }
}
