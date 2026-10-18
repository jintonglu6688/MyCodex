using System;
using System.Linq;
using System.Text;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class ChatHistorySpeechFormatter
    {
        private const int PreviewMaxUtf8Bytes = 4800;
        private const int PreviewMaxLines = 4;
        private const int PreviewMaxCharacters = 160;
        private static readonly string PreviewSuffix = UiLocalization.Text(
            "MainForm.ScreenReader.ManualItemPreviewSuffix",
            ". Content is long. Press Alt+Left/Right to read line by line, or Alt+Enter to open the message viewer.");

        internal static string BuildAutoReadText(ChatHistoryItem item)
        {
            return BuildAutoReadText(item, false);
        }

        internal static string BuildAutoReadText(ChatHistoryItem item, bool allowLongText)
        {
            return BuildSpeechText(item, allowLongText);
        }

        internal static string BuildBrowseText(ChatHistoryItem item)
        {
            return BuildBrowseText(item, false);
        }

        internal static string BuildBrowseText(ChatHistoryItem item, bool allowLongText)
        {
            return BuildSpeechText(item, allowLongText);
        }

        internal static string[] BuildReadableLines(ChatHistoryItem item)
        {
            var text = GetReadableSourceText(item);
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<string>();
            }

            if (ShouldSanitize(item))
            {
                return ChatMessageTextSanitizer.SanitizeForReadableLines(text);
            }

            return text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => (line ?? string.Empty).TrimEnd())
                .ToArray();
        }

        private static string BuildSpeechText(ChatHistoryItem item, bool allowLongText)
        {
            var normalized = BuildNormalizedSpeechText(item);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (allowLongText)
            {
                return normalized;
            }

            if (Encoding.UTF8.GetByteCount(normalized) <= PreviewMaxUtf8Bytes)
            {
                return normalized;
            }

            var lines = normalized
                .Split(new[] { '\n' }, StringSplitOptions.None)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(PreviewMaxLines)
                .Select(line => (line ?? string.Empty).TrimEnd())
                .ToArray();

            var preview = string.Join("; ", lines);
            if (string.IsNullOrWhiteSpace(preview))
            {
                preview = item.DisplayText ?? string.Empty;
            }

            if (preview.Length > PreviewMaxCharacters)
            {
                preview = preview.Substring(0, PreviewMaxCharacters).TrimEnd() + "...";
            }

            var combined = string.IsNullOrWhiteSpace(preview)
                ? PreviewSuffix.TrimStart('.')
                : preview + PreviewSuffix;

            return FitTextToUtf8Limit(combined, PreviewMaxUtf8Bytes);
        }

        private static string BuildNormalizedSpeechText(ChatHistoryItem item)
        {
            var fullText = GetReadableSourceText(item);
            if (string.IsNullOrWhiteSpace(fullText))
            {
                return string.Empty;
            }

            return ShouldSanitize(item)
                ? string.Join(Environment.NewLine, ChatMessageTextSanitizer.SanitizeForReadableLines(fullText)).Trim()
                : fullText
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Trim();
        }

        private static string GetReadableSourceText(ChatHistoryItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(item.AccessibleText))
            {
                return item.AccessibleText;
            }

            return string.IsNullOrWhiteSpace(item.FullText) ? item.DisplayText : item.FullText;
        }

        private static bool ShouldSanitize(ChatHistoryItem item)
        {
            return item != null && ChatMessageTextSanitizer.ShouldSanitizeSender(item.Sender);
        }

        private static string FitTextToUtf8Limit(string text, int maxUtf8Bytes)
        {
            if (string.IsNullOrEmpty(text) || maxUtf8Bytes <= 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var byteCount = 0;

            for (var index = 0; index < text.Length; index++)
            {
                var segmentLength = 1;
                if (char.IsHighSurrogate(text[index]) &&
                    index + 1 < text.Length &&
                    char.IsLowSurrogate(text[index + 1]))
                {
                    segmentLength = 2;
                }

                var segment = text.Substring(index, segmentLength);
                var segmentBytes = Encoding.UTF8.GetByteCount(segment);
                if (byteCount + segmentBytes > maxUtf8Bytes)
                {
                    break;
                }

                builder.Append(segment);
                byteCount += segmentBytes;

                if (segmentLength == 2)
                {
                    index++;
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}
