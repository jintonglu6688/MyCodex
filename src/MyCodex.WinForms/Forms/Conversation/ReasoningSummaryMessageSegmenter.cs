using System;
using System.Collections.Generic;
using System.Text;

namespace MyCodex.WinForms.Forms
{
    internal static class ReasoningSummaryMessageSegmenter
    {
        private const int MinimumSegmentLength = 4;

        internal static List<string> Append(ReasoningSummaryMessageState state, string summaryText, bool forceFlush)
        {
            var result = new List<string>();
            if (state == null)
            {
                return result;
            }

            var normalizedSummary = Normalize(summaryText);
            var delta = BuildDelta(state.LastSummaryText, normalizedSummary);
            if (delta.ResetPending)
            {
                state.PendingText = string.Empty;
            }

            state.LastSummaryText = normalizedSummary;
            if (!string.IsNullOrWhiteSpace(delta.Text))
            {
                state.PendingText = Normalize((state.PendingText ?? string.Empty) + delta.Text);
            }

            ExtractCompletedSegments(state, result);
            if (forceFlush)
            {
                FlushPendingSegment(state, result);
            }

            return result;
        }

        private static void ExtractCompletedSegments(ReasoningSummaryMessageState state, List<string> result)
        {
            while (!string.IsNullOrWhiteSpace(state.PendingText))
            {
                var boundaryIndex = FindSegmentBoundary(state.PendingText);
                if (boundaryIndex < 0)
                {
                    return;
                }

                var segment = state.PendingText.Substring(0, boundaryIndex + 1).Trim();
                state.PendingText = state.PendingText.Substring(boundaryIndex + 1).TrimStart();

                if (IsDisplayableSegment(segment))
                {
                    AddSegment(state, result, segment);
                    continue;
                }

                state.PendingText = (segment + " " + (state.PendingText ?? string.Empty)).Trim();
                return;
            }
        }

        private static void FlushPendingSegment(ReasoningSummaryMessageState state, List<string> result)
        {
            var segment = (state.PendingText ?? string.Empty).Trim();
            state.PendingText = string.Empty;
            if (IsDisplayableSegment(segment))
            {
                AddSegment(state, result, segment);
            }
        }

        private static void AddSegment(ReasoningSummaryMessageState state, List<string> result, string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return;
            }

            var normalized = CollapseWhitespace(segment);
            if (string.IsNullOrWhiteSpace(normalized) ||
                string.Equals(normalized, state.LastEmittedSegment, StringComparison.Ordinal))
            {
                return;
            }

            state.LastEmittedSegment = normalized;
            result.Add(normalized);
        }

        private static int FindSegmentBoundary(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '\n' ||
                    ch == '.' ||
                    ch == '!' ||
                    ch == '?' ||
                    ch == ';' ||
                    ch == '。' ||
                    ch == '！' ||
                    ch == '？' ||
                    ch == '；')
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsDisplayableSegment(string segment)
        {
            return CountNonWhiteSpaceCharacters(segment) >= MinimumSegmentLength;
        }

        private static int CountNonWhiteSpaceCharacters(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static Delta BuildDelta(string previousText, string currentText)
        {
            previousText = previousText ?? string.Empty;
            currentText = currentText ?? string.Empty;
            if (currentText.Length == 0)
            {
                return new Delta(string.Empty, false);
            }

            if (previousText.Length == 0)
            {
                return new Delta(currentText, false);
            }

            if (currentText.StartsWith(previousText, StringComparison.Ordinal))
            {
                return new Delta(currentText.Substring(previousText.Length), false);
            }

            var commonLength = GetCommonPrefixLength(previousText, currentText);
            return new Delta(currentText.Substring(commonLength), commonLength < previousText.Length);
        }

        private static int GetCommonPrefixLength(string first, string second)
        {
            var max = Math.Min(first == null ? 0 : first.Length, second == null ? 0 : second.Length);
            var index = 0;
            while (index < max && first[index] == second[index])
            {
                index++;
            }

            return index;
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string CollapseWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var previousWasWhitespace = false;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (char.IsWhiteSpace(ch))
                {
                    if (!previousWasWhitespace)
                    {
                        builder.Append(' ');
                    }

                    previousWasWhitespace = true;
                    continue;
                }

                builder.Append(ch);
                previousWasWhitespace = false;
            }

            return builder.ToString().Trim();
        }

        private struct Delta
        {
            public Delta(string text, bool resetPending)
            {
                Text = text;
                ResetPending = resetPending;
            }

            public string Text { get; private set; }

            public bool ResetPending { get; private set; }
        }
    }

    internal sealed class ReasoningSummaryMessageState
    {
        public string LastSummaryText { get; set; }

        public string PendingText { get; set; }

        public string LastEmittedSegment { get; set; }
    }
}
