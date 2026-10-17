using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Controls
{
    internal sealed class AttachmentComposerTextBox : ThemedTextBox
    {
        private readonly List<TrackedAttachment> _attachments = new List<TrackedAttachment>();
        private string _previousText = string.Empty;
        private bool _isResettingText;

        internal AttachmentComposerTextBox()
        {
            AcceptsReturn = true;
            AcceptsTab = false;
            BorderStyle = BorderStyle.FixedSingle;
            HideSelection = false;
            ImeMode = ImeMode.On;
            MaxLength = int.MaxValue;
            Multiline = true;
            ScrollBars = ScrollBars.Both;
            ShortcutsEnabled = true;
            TabStop = true;
            WordWrap = false;
            ApplyTheme();
        }

        internal int AccessibleCaretOffset
        {
            get { return SelectionStart + SelectionLength; }
        }

        internal string ComposeText
        {
            get { return SerializeVisualText(); }
        }

        internal void SetComposeText(
            string text,
            IEnumerable<PendingAttachmentItem> attachments,
            int selectionStart,
            int selectionLength)
        {
            var sourceText = text ?? string.Empty;
            var normalized = TextBoxLineEndingNormalizer.NormalizeMultilineText(sourceText);
            var visualDocument = BuildVisualDocument(normalized, attachments);
            _isResettingText = true;
            try
            {
                Text = visualDocument.Text;
            }
            finally
            {
                _isResettingText = false;
            }

            _previousText = Text ?? string.Empty;
            _attachments.Clear();
            _attachments.AddRange(visualDocument.Attachments);

            var sourceStart = Math.Max(0, Math.Min(selectionStart, sourceText.Length));
            var normalizedSelectionLength = Math.Max(0, selectionLength);
            var sourceEnd = normalizedSelectionLength > sourceText.Length - sourceStart
                ? sourceText.Length
                : sourceStart + normalizedSelectionLength;
            var normalizedStart = MapIndexToNormalizedText(sourceText, sourceStart);
            var normalizedEnd = MapIndexToNormalizedText(sourceText, sourceEnd);
            var start = MapComposeIndexToVisual(normalizedStart);
            var end = MapComposeIndexToVisual(normalizedEnd);
            var length = Math.Max(0, end - start);
            Select(start, length);
            ClearUndo();
        }

        internal void InsertAttachments(IEnumerable<PendingAttachmentItem> attachments)
        {
            if (ReadOnly)
            {
                return;
            }

            var insertedAttachments = (attachments ?? Array.Empty<PendingAttachmentItem>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.MarkerText))
                .ToArray();
            if (insertedAttachments.Length == 0)
            {
                return;
            }

            var selectionStart = SelectionStart;
            var selectionEnd = selectionStart + SelectionLength;
            var hasSyntheticLeadingLineBreak = selectionStart > 0 && !IsLineBreakBefore(Text, selectionStart);
            var hasSyntheticTrailingLineBreak = !IsLineBreakAt(Text, selectionEnd);
            var insertion = (hasSyntheticLeadingLineBreak ? Environment.NewLine : string.Empty) +
                string.Join(Environment.NewLine, insertedAttachments.Select(item => item.MarkerText)) +
                (hasSyntheticTrailingLineBreak ? Environment.NewLine : string.Empty);
            var markerStart = selectionStart + (hasSyntheticLeadingLineBreak ? Environment.NewLine.Length : 0);
            SelectedText = insertion;
            for (var index = 0; index < insertedAttachments.Length; index++)
            {
                var attachment = insertedAttachments[index];
                _attachments.Add(new TrackedAttachment(
                    attachment,
                    markerStart,
                    index == 0 && hasSyntheticLeadingLineBreak,
                    index < insertedAttachments.Length - 1 || hasSyntheticTrailingLineBreak));
                markerStart += attachment.MarkerText.Length + Environment.NewLine.Length;
            }
        }

        internal void RemoveAttachment(PendingAttachmentItem attachment)
        {
            if (ReadOnly || attachment == null || string.IsNullOrWhiteSpace(attachment.MarkerText))
            {
                return;
            }

            var trackedAttachment = FindTrackedAttachment(attachment);
            if (trackedAttachment == null)
            {
                _attachments.RemoveAll(item => SameAttachment(item.Attachment, attachment));
                return;
            }

            RemoveMarker(trackedAttachment);
            _attachments.RemoveAll(item => SameAttachment(item.Attachment, attachment));
        }

        internal void RemoveAllAttachments()
        {
            if (ReadOnly)
            {
                return;
            }

            var markers = _attachments
                .Where(item => item.Attachment != null && !string.IsNullOrWhiteSpace(item.Attachment.MarkerText))
                .Where(item => item.Index >= 0)
                .OrderByDescending(item => item.Index)
                .ToArray();

            foreach (var marker in markers)
            {
                RemoveMarker(marker);
            }

            _attachments.Clear();
        }

        internal void InsertNewLine()
        {
            if (!ReadOnly)
            {
                SelectedText = Environment.NewLine;
            }
        }

        internal bool ProcessCommandKeyForTests(Keys keyData)
        {
            var message = Message.Create(Handle, 0, IntPtr.Zero, IntPtr.Zero);
            return ProcessCmdKey(ref message, keyData);
        }

        internal void ApplyTheme()
        {
            BackColor = UiTheme.CardBackColor;
            ForeColor = UiTheme.TextColor;
            Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab))
            {
                return ProcessDialogKey(keyData);
            }

            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            var currentText = Text ?? string.Empty;
            if (!_isResettingText)
            {
                UpdateTrackedAttachments(_previousText, currentText);
            }

            _previousText = currentText;
            base.OnTextChanged(e);
            _previousText = Text ?? string.Empty;
        }

        private static VisualDocument BuildVisualDocument(
            string composeText,
            IEnumerable<PendingAttachmentItem> attachments)
        {
            composeText = composeText ?? string.Empty;
            var markerPositions = new List<ComposeMarker>();
            var occupiedIndexes = new HashSet<int>();
            foreach (var attachment in attachments ?? Array.Empty<PendingAttachmentItem>())
            {
                if (attachment == null || string.IsNullOrWhiteSpace(attachment.MarkerText))
                {
                    continue;
                }

                var searchIndex = 0;
                while (searchIndex <= composeText.Length - attachment.MarkerText.Length)
                {
                    var markerIndex = composeText.IndexOf(attachment.MarkerText, searchIndex, StringComparison.Ordinal);
                    if (markerIndex < 0)
                    {
                        break;
                    }

                    if (occupiedIndexes.Add(markerIndex))
                    {
                        markerPositions.Add(new ComposeMarker(attachment, markerIndex));
                        break;
                    }

                    searchIndex = markerIndex + attachment.MarkerText.Length;
                }
            }

            var builder = new System.Text.StringBuilder();
            var trackedAttachments = new List<TrackedAttachment>();
            var sourceIndex = 0;
            TrackedAttachment previousAttachment = null;
            foreach (var marker in markerPositions.OrderBy(item => item.Index))
            {
                var gap = composeText.Substring(sourceIndex, marker.Index - sourceIndex);
                if (previousAttachment == null ||
                    !previousAttachment.HasSyntheticTrailingLineBreak ||
                    !IsHorizontalWhitespace(gap))
                {
                    builder.Append(gap);
                }

                var hasSyntheticLeadingLineBreak = builder.Length > 0 && !EndsWithLineBreak(builder);
                if (hasSyntheticLeadingLineBreak)
                {
                    builder.Append(Environment.NewLine);
                }

                var visualMarkerIndex = builder.Length;
                builder.Append(marker.Attachment.MarkerText);
                var markerEnd = marker.Index + marker.Attachment.MarkerText.Length;
                var hasSyntheticTrailingLineBreak = !IsLineBreakAt(composeText, markerEnd);
                if (hasSyntheticTrailingLineBreak)
                {
                    builder.Append(Environment.NewLine);
                }

                previousAttachment = new TrackedAttachment(
                    marker.Attachment,
                    visualMarkerIndex,
                    hasSyntheticLeadingLineBreak,
                    hasSyntheticTrailingLineBreak);
                trackedAttachments.Add(previousAttachment);
                sourceIndex = markerEnd;
            }

            builder.Append(composeText, sourceIndex, composeText.Length - sourceIndex);
            return new VisualDocument(builder.ToString(), trackedAttachments);
        }

        private TrackedAttachment FindTrackedAttachment(PendingAttachmentItem attachment)
        {
            return _attachments.FirstOrDefault(item => SameAttachment(item.Attachment, attachment));
        }

        private string SerializeVisualText()
        {
            var text = Text ?? string.Empty;
            if (_attachments.Count == 0)
            {
                return text;
            }

            var actions = BuildLayoutActions(text);
            if (actions.Count == 0)
            {
                return text;
            }

            var builder = new System.Text.StringBuilder(text.Length);
            for (var index = 0; index < text.Length;)
            {
                LayoutAction action;
                if (actions.TryGetValue(index, out action))
                {
                    builder.Append(action.Replacement);
                    index += action.Length;
                    continue;
                }

                builder.Append(text[index]);
                index++;
            }

            return builder.ToString();
        }

        private int MapComposeIndexToVisual(int composeIndex)
        {
            composeIndex = Math.Max(0, composeIndex);
            var text = Text ?? string.Empty;
            var actions = BuildLayoutActions(text);
            var composeOffset = 0;
            for (var visualOffset = 0; visualOffset < text.Length;)
            {
                LayoutAction action;
                if (actions.TryGetValue(visualOffset, out action))
                {
                    if (action.Kind == LayoutActionKind.SyntheticLeading)
                    {
                        visualOffset += action.Length;
                        continue;
                    }

                    if (composeOffset >= composeIndex)
                    {
                        return visualOffset;
                    }

                    if (!string.IsNullOrEmpty(action.Replacement))
                    {
                        composeOffset += action.Replacement.Length;
                    }

                    visualOffset += action.Length;
                    if (composeOffset >= composeIndex)
                    {
                        return visualOffset;
                    }

                    continue;
                }

                if (composeOffset >= composeIndex)
                {
                    return visualOffset;
                }

                composeOffset++;
                visualOffset++;
            }

            return text.Length;
        }

        private Dictionary<int, LayoutAction> BuildLayoutActions(string text)
        {
            var actions = new Dictionary<int, LayoutAction>();
            var orderedAttachments = _attachments.OrderBy(item => item.Index).ToArray();
            for (var index = 0; index < orderedAttachments.Length; index++)
            {
                var tracked = orderedAttachments[index];
                if (!IsTrackedMarkerPresent(text, tracked))
                {
                    continue;
                }

                if (tracked.HasSyntheticLeadingLineBreak)
                {
                    var lineBreakStart = GetLineBreakStartBefore(text, tracked.Index);
                    if (lineBreakStart >= 0)
                    {
                        actions[lineBreakStart] = new LayoutAction(
                            tracked.Index - lineBreakStart,
                            string.Empty,
                            LayoutActionKind.SyntheticLeading);
                    }
                }

                if (!tracked.HasSyntheticTrailingLineBreak)
                {
                    continue;
                }

                var markerEnd = tracked.Index + tracked.Attachment.MarkerText.Length;
                var lineBreakLength = LineBreakLengthAt(text, markerEnd);
                if (lineBreakLength == 0)
                {
                    continue;
                }

                var nextAttachmentStartsAfterBreak = index + 1 < orderedAttachments.Length &&
                    orderedAttachments[index + 1].Index == markerEnd + lineBreakLength;
                actions[markerEnd] = new LayoutAction(
                    lineBreakLength,
                    nextAttachmentStartsAfterBreak ? " " : string.Empty,
                    LayoutActionKind.SyntheticTrailing);
            }

            return actions;
        }

        private void UpdateTrackedAttachments(string previousText, string currentText)
        {
            previousText = previousText ?? string.Empty;
            currentText = currentText ?? string.Empty;
            if (string.Equals(previousText, currentText, StringComparison.Ordinal) || _attachments.Count == 0)
            {
                return;
            }

            var prefixLength = 0;
            var sharedLength = Math.Min(previousText.Length, currentText.Length);
            while (prefixLength < sharedLength && previousText[prefixLength] == currentText[prefixLength])
            {
                prefixLength++;
            }

            var suffixLength = 0;
            while (suffixLength < previousText.Length - prefixLength &&
                   suffixLength < currentText.Length - prefixLength &&
                   previousText[previousText.Length - suffixLength - 1] == currentText[currentText.Length - suffixLength - 1])
            {
                suffixLength++;
            }

            var previousChangeEnd = previousText.Length - suffixLength;
            var delta = currentText.Length - previousText.Length;
            for (var index = _attachments.Count - 1; index >= 0; index--)
            {
                var tracked = _attachments[index];
                var markerEnd = tracked.Index + tracked.Attachment.MarkerText.Length;
                if (markerEnd <= prefixLength)
                {
                    continue;
                }

                if (tracked.Index >= previousChangeEnd)
                {
                    tracked.Index += delta;
                    continue;
                }

                _attachments.RemoveAt(index);
            }
        }

        private static bool SameAttachment(PendingAttachmentItem left, PendingAttachmentItem right)
        {
            return ReferenceEquals(left, right) ||
                   left != null && right != null &&
                   string.Equals(left.LocalPath, right.LocalPath, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.MarkerText, right.MarkerText, StringComparison.Ordinal);
        }

        private static int MapIndexToNormalizedText(string sourceText, int sourceIndex)
        {
            if (sourceIndex <= 0)
            {
                return 0;
            }

            return TextBoxLineEndingNormalizer.NormalizeMultilineText(
                sourceText.Substring(0, Math.Min(sourceIndex, sourceText.Length))).Length;
        }

        private void RemoveMarker(TrackedAttachment tracked)
        {
            var text = Text ?? string.Empty;
            var markerIndex = tracked.Index;
            var markerLength = tracked.Attachment.MarkerText.Length;
            var removalStart = markerIndex;
            var removalEnd = markerIndex + markerLength;

            if (tracked.HasSyntheticLeadingLineBreak)
            {
                var lineBreakStart = GetLineBreakStartBefore(text, removalStart);
                if (lineBreakStart >= 0)
                {
                    removalStart = lineBreakStart;
                }
            }

            if (tracked.HasSyntheticTrailingLineBreak)
            {
                removalEnd += LineBreakLengthAt(text, removalEnd);
            }

            if (!tracked.HasSyntheticLeadingLineBreak && !tracked.HasSyntheticTrailingLineBreak)
            {
                var lineStart = GetLineStart(text, markerIndex);
                var lineEnd = GetLineEnd(text, removalEnd);
                var markerOccupiesLine = string.IsNullOrWhiteSpace(text.Substring(lineStart, markerIndex - lineStart)) &&
                    string.IsNullOrWhiteSpace(text.Substring(removalEnd, lineEnd - removalEnd));
                if (markerOccupiesLine && IsLineBreakAt(text, lineEnd))
                {
                    removalStart = lineStart;
                    removalEnd = lineEnd + LineBreakLengthAt(text, lineEnd);
                }
                else if (markerOccupiesLine && lineStart > 0)
                {
                    removalStart = GetLineBreakStartBefore(text, lineStart);
                    removalEnd = lineEnd;
                }
            }

            if (removalStart > 0 && removalEnd < text.Length &&
                text[removalStart - 1] == ' ' && text[removalEnd] == ' ')
            {
                removalEnd++;
            }

            Select(removalStart, removalEnd - removalStart);
            SelectedText = string.Empty;
        }

        private static bool IsLineBreakBefore(string text, int index)
        {
            return index > 0 && (text[index - 1] == '\n' || text[index - 1] == '\r');
        }

        private static bool IsLineBreakAt(string text, int index)
        {
            return index >= 0 && index < text.Length && (text[index] == '\r' || text[index] == '\n');
        }

        private static int LineBreakLengthAt(string text, int index)
        {
            if (!IsLineBreakAt(text, index))
            {
                return 0;
            }

            return index + 1 < text.Length && text[index] == '\r' && text[index + 1] == '\n' ? 2 : 1;
        }

        private static int GetLineBreakStartBefore(string text, int index)
        {
            if (string.IsNullOrEmpty(text) || index <= 0 || index > text.Length)
            {
                return -1;
            }

            if (text[index - 1] == '\n')
            {
                return index > 1 && text[index - 2] == '\r' ? index - 2 : index - 1;
            }

            return text[index - 1] == '\r' ? index - 1 : -1;
        }

        private static int GetLineStart(string text, int index)
        {
            index = Math.Max(0, Math.Min(index, text.Length));
            var lineBreak = text.LastIndexOf('\n', Math.Max(0, index - 1));
            return lineBreak >= 0 ? lineBreak + 1 : 0;
        }

        private static int GetLineEnd(string text, int index)
        {
            index = Math.Max(0, Math.Min(index, text.Length));
            while (index < text.Length && text[index] != '\r' && text[index] != '\n')
            {
                index++;
            }

            return index;
        }

        private static bool EndsWithLineBreak(System.Text.StringBuilder builder)
        {
            return builder.Length > 0 && (builder[builder.Length - 1] == '\r' || builder[builder.Length - 1] == '\n');
        }

        private static bool IsHorizontalWhitespace(string value)
        {
            return !string.IsNullOrEmpty(value) && value.All(character => character == ' ' || character == '\t');
        }

        private static bool IsTrackedMarkerPresent(string text, TrackedAttachment tracked)
        {
            return tracked != null &&
                   tracked.Attachment != null &&
                   tracked.Index >= 0 &&
                   tracked.Index + tracked.Attachment.MarkerText.Length <= text.Length &&
                   string.Compare(
                       text,
                       tracked.Index,
                       tracked.Attachment.MarkerText,
                       0,
                       tracked.Attachment.MarkerText.Length,
                       StringComparison.Ordinal) == 0;
        }

        private sealed class TrackedAttachment
        {
            internal TrackedAttachment(
                PendingAttachmentItem attachment,
                int index,
                bool hasSyntheticLeadingLineBreak,
                bool hasSyntheticTrailingLineBreak)
            {
                Attachment = attachment;
                Index = index;
                HasSyntheticLeadingLineBreak = hasSyntheticLeadingLineBreak;
                HasSyntheticTrailingLineBreak = hasSyntheticTrailingLineBreak;
            }

            internal PendingAttachmentItem Attachment { get; }

            internal int Index { get; set; }

            internal bool HasSyntheticLeadingLineBreak { get; }

            internal bool HasSyntheticTrailingLineBreak { get; }
        }

        private sealed class ComposeMarker
        {
            internal ComposeMarker(PendingAttachmentItem attachment, int index)
            {
                Attachment = attachment;
                Index = index;
            }

            internal PendingAttachmentItem Attachment { get; }

            internal int Index { get; }
        }

        private sealed class VisualDocument
        {
            internal VisualDocument(string text, IReadOnlyList<TrackedAttachment> attachments)
            {
                Text = text ?? string.Empty;
                Attachments = attachments ?? Array.Empty<TrackedAttachment>();
            }

            internal string Text { get; }

            internal IReadOnlyList<TrackedAttachment> Attachments { get; }
        }

        private sealed class LayoutAction
        {
            internal LayoutAction(int length, string replacement, LayoutActionKind kind)
            {
                Length = length;
                Replacement = replacement ?? string.Empty;
                Kind = kind;
            }

            internal int Length { get; }

            internal string Replacement { get; }

            internal LayoutActionKind Kind { get; }
        }

        private enum LayoutActionKind
        {
            SyntheticLeading,
            SyntheticTrailing
        }
    }
}
