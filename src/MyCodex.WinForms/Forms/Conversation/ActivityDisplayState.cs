using System;
using System.Collections.Generic;
using System.Linq;

namespace MyCodex.WinForms.Forms
{
    internal static class ActivityDisplayState
    {
        internal static string BuildCurrentStatus(string summaryText, string emptyText)
        {
            var lines = SplitReadableLines(summaryText);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (IsSectionHeaderLine(line))
                {
                    continue;
                }

                if (IsLiveActivityLine(line) || i == 0)
                {
                    return line;
                }
            }

            return emptyText ?? string.Empty;
        }

        internal static string[] BuildStableItems(string summaryText, string emptyText)
        {
            var items = new List<string>();
            var currentSection = string.Empty;
            var lines = SplitReadableLines(summaryText);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (IsSectionHeaderLine(line))
                {
                    currentSection = TrimSectionHeader(line);
                    continue;
                }

                if (IsLiveActivityLine(line))
                {
                    continue;
                }

                if (!IsStableActivityLine(line))
                {
                    continue;
                }

                items.Add(string.IsNullOrWhiteSpace(currentSection)
                    ? line
                    : FormatSectionLine(currentSection, line));
            }

            if (items.Count == 0 && !string.IsNullOrWhiteSpace(emptyText))
            {
                items.Add(emptyText.Trim());
            }

            return items.ToArray();
        }

        private static string FormatSectionLine(string prefix, string line)
        {
            return string.IsNullOrEmpty(prefix)
                ? line
                : prefix + ": " + line;
        }

        private static bool IsSectionHeaderLine(string line)
        {
            var text = (line ?? string.Empty).Trim();
            return text.Length > 1 &&
                   (text.EndsWith(":", StringComparison.Ordinal) ||
                    text.EndsWith("\uff1a", StringComparison.Ordinal));
        }

        private static string TrimSectionHeader(string line)
        {
            var text = (line ?? string.Empty).Trim();
            return text.EndsWith(":", StringComparison.Ordinal) ||
                   text.EndsWith("\uff1a", StringComparison.Ordinal)
                ? text.Substring(0, text.Length - 1)
                : text;
        }

        private static bool IsSelfLabeledLine(string line)
        {
            var text = (line ?? string.Empty).Trim();
            var colonIndex = text.IndexOf(':');
            var fullWidthColonIndex = text.IndexOf('\uff1a');
            var index = colonIndex >= 0
                ? colonIndex
                : fullWidthColonIndex;
            return index > 0 && index <= 24;
        }

        private static bool IsLiveActivityLine(string line)
        {
            var text = (line ?? string.Empty).Trim();
            return StartsWithLabel(text, "Current step") ||
                   StartsWithLabel(text, "\u5f53\u524d\u6b65\u9aa4") ||
                   StartsWithLabel(text, "Current") ||
                   StartsWithLabel(text, "\u5f53\u524d") ||
                   StartsWithLabel(text, "In Progress") ||
                   StartsWithLabel(text, "\u8fdb\u884c\u4e2d") ||
                   StartsWithLabel(text, "Pending") ||
                   StartsWithLabel(text, "\u5f85\u5f00\u59cb") ||
                   StartsWithLabel(text, "Step") ||
                   StartsWithLabel(text, "\u6b65\u9aa4") ||
                   StartsWithLabel(text, "Running") ||
                   StartsWithLabel(text, "\u6b63\u5728");
        }

        private static bool IsStableActivityLine(string line)
        {
            var text = (line ?? string.Empty).Trim();
            return StartsWithLabel(text, "Completed") ||
                   StartsWithLabel(text, "\u5b8c\u6210") ||
                   StartsWithLabel(text, "Modified") ||
                   StartsWithLabel(text, "\u5df2\u4fee\u6539") ||
                   StartsWithLabel(text, "A summary") ||
                   StartsWithLabel(text, "\u5df2\u6536\u5230") ||
                   StartsWithLabel(text, "Failed") ||
                   StartsWithLabel(text, "\u5931\u8d25") ||
                   StartsWithLabel(text, "Error") ||
                   StartsWithLabel(text, "\u9519\u8bef") ||
                   !IsSelfLabeledLine(text);
        }

        private static bool StartsWithLabel(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

            return text.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith(label + "\uff1a", StringComparison.OrdinalIgnoreCase);
        }

        private static string[] SplitReadableLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new string[0];
            }

            return text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();
        }
    }
}
