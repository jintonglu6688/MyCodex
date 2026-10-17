using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public class StructuredMessageViewerForm : Form
    {
        private static readonly Regex MarkdownLinkRegex = new Regex(@"\[(?<text>[^\]\r\n]+)\]\((?<url>(?:https?://|mailto:)[^)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RawUrlRegex = new Regex(@"(?<url>(?:https?://|mailto:)[^\s<>()]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FencedCodeBlockRegex = new Regex(@"```(?<language>[^\r\n`]*)\r?\n(?<code>[\s\S]*?)(?:\r?\n)?```", RegexOptions.Compiled);
        private static readonly Regex InlineCodeRegex = new Regex(@"`(?<code>[^`\r\n]+)`", RegexOptions.Compiled);
        private static readonly Regex HeadingRegex = new Regex(@"^\s{0,3}#{1,6}\s+(?<text>\S.*)$", RegexOptions.Compiled);
        private static readonly Regex UnorderedListRegex = new Regex(@"^(?<indent>\s*)[-*+]\s+(?<text>.+)$", RegexOptions.Compiled);
        private static readonly Regex QuoteRegex = new Regex(@"^\s{0,3}>\s?(?<text>.*)$", RegexOptions.Compiled);
        private static readonly Regex TableSeparatorRegex = new Regex(@"^\s*\|?[\s:\-]+(?:\|[\s:\-]+)+\|?\s*$", RegexOptions.Compiled);

        private readonly ChatHistoryItem _item;
        private readonly RichTextBox _contentBox;
        private readonly ContextMenu _contextMenu;
        private readonly MenuItem _openLinkMenuItem;
        private readonly MenuItem _copyLinkMenuItem;
        private readonly MenuItem _copyCodeMenuItem;
        private readonly MenuItem _copySelectionMenuItem;
        private readonly MenuItem _closeMenuItem;
        private readonly Font _bodyFont;
        private readonly Font _headingFont;
        private readonly Font _linkFont;
        private readonly Font _codeFont;
        private readonly Font _interactiveCodeFont;
        private readonly List<InteractiveSpan> _interactiveSpans;
        private readonly List<CodeBlockSpan> _codeBlockSpans;

        private InteractiveSpan _currentInteractiveSpan;
        private CodeBlockSpan _currentCodeBlockSpan;

        public StructuredMessageViewerForm(ChatHistoryItem item)
        {
            _item = item ?? new ChatHistoryItem();
            _interactiveSpans = new List<InteractiveSpan>();
            _codeBlockSpans = new List<CodeBlockSpan>();
            _bodyFont = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular);
            _headingFont = UiTheme.CreateUiFont(UiTheme.SubtitleFontSize, FontStyle.Bold);
            _linkFont = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Underline);
            _codeFont = UiTheme.CreateCodeFont(UiTheme.ReadingFontSize, FontStyle.Regular);
            _interactiveCodeFont = UiTheme.CreateCodeFont(UiTheme.ReadingFontSize, FontStyle.Underline);
            _contextMenu = new ContextMenu();
            _openLinkMenuItem = new MenuItem(UiLocalization.Text("StructuredMessageViewerForm.Menu.OpenLink", "Open Link(&O)"), OpenLinkMenuItem_Click);
            _copyLinkMenuItem = new MenuItem(UiLocalization.Text("StructuredMessageViewerForm.Menu.CopyLink", "Copy Link(&L)"), CopyLinkMenuItem_Click);
            _copyCodeMenuItem = new MenuItem(UiLocalization.Text("StructuredMessageViewerForm.Menu.CopyCode", "Copy Code(&C)"), CopyCodeMenuItem_Click);
            _copySelectionMenuItem = new MenuItem(UiLocalization.Text("StructuredMessageViewerForm.Menu.CopySelection", "Copy Selected Text(&Y)"), CopySelectionMenuItem_Click);
            _closeMenuItem = new MenuItem(UiLocalization.Text("StructuredMessageViewerForm.Menu.Close", "Close(&X)"), CloseMenuItem_Click);
            _contentBox = new RichTextBox();

            SuspendLayout();
            UiTheme.ApplyForm(this);

            _contentBox.AccessibleName = UiLocalization.Text("StructuredMessageViewerForm.Accessible.Content", "Message Content");
            _contentBox.BackColor = UiTheme.CardBackColor;
            _contentBox.BorderStyle = BorderStyle.None;
            _contentBox.ContextMenu = _contextMenu;
            _contentBox.DetectUrls = false;
            _contentBox.Dock = DockStyle.Fill;
            _contentBox.Font = _bodyFont;
            _contentBox.ForeColor = UiTheme.TextColor;
            _contentBox.HideSelection = false;
            _contentBox.ReadOnly = true;
            _contentBox.ScrollBars = RichTextBoxScrollBars.Both;
            _contentBox.ShortcutsEnabled = true;
            _contentBox.TabIndex = 0;
            _contentBox.WordWrap = false;
            _contentBox.KeyDown += ContentBox_KeyDown;
            _contentBox.MouseDown += ContentBox_MouseDown;
            _contentBox.MouseDoubleClick += ContentBox_MouseDoubleClick;
            _contentBox.MouseMove += ContentBox_MouseMove;
            _contextMenu.Popup += ContextMenu_Popup;

            ClientSize = new Size(760, 560);
            Controls.Add(_contentBox);
            KeyPreview = true;
            MinimizeBox = false;
            MinimumSize = new Size(480, 320);
            Name = "StructuredMessageViewerForm";
            Padding = new Padding(12);
            StartPosition = FormStartPosition.CenterParent;
            Text = string.IsNullOrWhiteSpace(_item.Sender)
                ? UiLocalization.Text("StructuredMessageViewerForm.Title.Content", "Message Content")
                : string.Format(
                    CultureInfo.InvariantCulture,
                    UiLocalization.Text("StructuredMessageViewerForm.Title.SenderContent", "{0} Message Content"),
                    _item.Sender);

            RenderDocument();
            Shown += StructuredMessageViewerForm_Shown;

            ResumeLayout(false);
            PerformLayout();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bodyFont.Dispose();
                _headingFont.Dispose();
                _linkFont.Dispose();
                _codeFont.Dispose();
                _interactiveCodeFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private void StructuredMessageViewerForm_Shown(object sender, EventArgs e)
        {
            _contentBox.Focus();
            _contentBox.SelectionStart = 0;
            _contentBox.SelectionLength = 0;
        }

        private void RenderDocument()
        {
            _interactiveSpans.Clear();
            _codeBlockSpans.Clear();
            _contentBox.Clear();
            RenderStructuredText(NormalizeForViewer(GetViewerText(_item)));
            _contentBox.SelectionStart = 0;
            _contentBox.SelectionLength = 0;
        }

        private void RenderStructuredText(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                AppendText(
                    UiLocalization.Text("StructuredMessageViewerForm.Text.NoDisplayContent", "No displayable content in the current message."),
                    _bodyFont,
                    UiTheme.MutedTextColor,
                    null);
                return;
            }

            var lastIndex = 0;
            var codeBlockIndex = 0;
            foreach (Match match in FencedCodeBlockRegex.Matches(content))
            {
                if (match.Index > lastIndex)
                {
                    RenderTextBlock(content.Substring(lastIndex, match.Index - lastIndex));
                }

                codeBlockIndex++;
                RenderCodeBlock(
                    codeBlockIndex,
                    (match.Groups["language"].Value ?? string.Empty).Trim(),
                    NormalizeForViewer(match.Groups["code"].Value).Replace("\n", Environment.NewLine));
                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < content.Length)
            {
                RenderTextBlock(content.Substring(lastIndex));
            }
        }

        private void RenderTextBlock(string text)
        {
            var lines = NormalizeForViewer(text).Replace("\n", Environment.NewLine)
                .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            for (var i = 0; i < lines.Length; i++)
            {
                RenderLine(lines[i] ?? string.Empty);
                if (i < lines.Length - 1)
                {
                    AppendLineBreak();
                }
            }
        }

        private void RenderLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            var rendered = line;
            var font = _bodyFont;
            var color = UiTheme.TextColor;
            Match match;

            if ((match = HeadingRegex.Match(line)).Success)
            {
                rendered = match.Groups["text"].Value;
                font = _headingFont;
            }
            else if ((match = QuoteRegex.Match(line)).Success)
            {
                rendered = UiLocalization.Text("StructuredMessageViewerForm.Text.QuotePrefix", "Quote: ") + match.Groups["text"].Value;
                color = UiTheme.MutedTextColor;
            }
            else if ((match = UnorderedListRegex.Match(line)).Success)
            {
                rendered = (match.Groups["indent"].Value ?? string.Empty) + "• " + match.Groups["text"].Value;
            }
            else if (TableSeparatorRegex.IsMatch(line))
            {
                return;
            }

            RenderInlineContent(rendered, font, color);
        }

        private void RenderInlineContent(string text, Font font, Color color)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var index = 0;
            while (index < text.Length)
            {
                var token = FindNextInlineToken(text, index);
                if (token == null)
                {
                    AppendText(text.Substring(index), font, color, null);
                    break;
                }

                if (token.Index > index)
                {
                    AppendText(text.Substring(index, token.Index - index), font, color, null);
                }

                if (token.Kind == InlineTokenKind.InlineCode)
                {
                    AppendInteractiveText(
                        token.DisplayText,
                        InteractiveKind.CopyCode,
                        token.Value,
                        _interactiveCodeFont,
                        GetLinkColor(),
                        GetCodeBlockBackColor());
                }
                else
                {
                    var linkText = token.Kind == InlineTokenKind.MarkdownLink
                        ? string.Format(
                            CultureInfo.InvariantCulture,
                            UiLocalization.Text("StructuredMessageViewerForm.Text.LinkSuffixTemplate", "{0} (Link)"),
                            token.DisplayText)
                        : token.DisplayText;
                    AppendInteractiveText(
                        linkText,
                        InteractiveKind.Link,
                        token.Value,
                        _linkFont,
                        GetLinkColor(),
                        null);
                }

                index = token.Index + token.Length;
            }
        }

        private void RenderCodeBlock(int index, string language, string codeText)
        {
            EnsureSeparatedFromPreviousContent();
            AppendInteractiveText(
                string.Format(
                    CultureInfo.InvariantCulture,
                    UiLocalization.Text("StructuredMessageViewerForm.Text.CopyCodeBlockTemplate", "Copy Code Block {0}"),
                    index),
                InteractiveKind.CopyCode,
                codeText,
                _linkFont,
                GetLinkColor(),
                null);
            AppendLineBreak();
            AppendText(
                string.IsNullOrWhiteSpace(language)
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        UiLocalization.Text("StructuredMessageViewerForm.Text.CodeBlockTemplate", "Code Block {0}"),
                        index)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        UiLocalization.Text("StructuredMessageViewerForm.Text.CodeBlockLanguageTemplate", "Code Block {0} ({1})"),
                        index,
                        language),
                _headingFont,
                UiTheme.MutedTextColor,
                null);
            AppendLineBreak();

            var start = _contentBox.TextLength;
            AppendCodeTextLines(codeText, _codeFont, UiTheme.TextColor, GetCodeBlockBackColor());
            var length = _contentBox.TextLength - start;
            if (length > 0)
            {
                _codeBlockSpans.Add(new CodeBlockSpan { Start = start, Length = length, CodeText = codeText });
            }

            AppendLineBreak();
            AppendLineBreak();
        }

        private void AppendCodeTextLines(string codeText, Font font, Color color, Color? backColor)
        {
            var lines = NormalizeForViewer(codeText).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    AppendLineBreak();
                }

                AppendText(lines[i] ?? string.Empty, font, color, backColor);
            }
        }

        private void AppendText(string text, Font font, Color color, Color? backColor)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _contentBox.SelectionStart = _contentBox.TextLength;
            _contentBox.SelectionLength = 0;
            _contentBox.SelectionFont = font ?? _bodyFont;
            _contentBox.SelectionColor = color;
            _contentBox.SelectionBackColor = backColor ?? _contentBox.BackColor;
            _contentBox.SelectedText = text;
        }

        private void AppendInteractiveText(string text, InteractiveKind kind, string payload, Font font, Color color, Color? backColor)
        {
            var start = _contentBox.TextLength;
            AppendText(text, font, color, backColor);
            var length = _contentBox.TextLength - start;
            if (length > 0)
            {
                _interactiveSpans.Add(new InteractiveSpan { Start = start, Length = length, Kind = kind, Payload = payload ?? string.Empty });
            }
        }

        private void AppendLineBreak()
        {
            AppendText(Environment.NewLine, _bodyFont, UiTheme.TextColor, null);
        }

        private static Color GetLinkColor()
        {
            return UiTheme.CurrentMode == UiThemeMode.Dark
                ? Color.FromArgb(147, 197, 253)
                : Color.FromArgb(11, 99, 206);
        }

        private static Color GetCodeBlockBackColor()
        {
            return UiTheme.CurrentMode == UiThemeMode.Dark
                ? UiTheme.SubtleBackColor
                : Color.FromArgb(243, 245, 248);
        }

        private void EnsureSeparatedFromPreviousContent()
        {
            if (_contentBox.TextLength == 0)
            {
                return;
            }

            if (_contentBox.Text.EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal))
            {
                return;
            }

            if (_contentBox.Text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                AppendLineBreak();
                return;
            }

            AppendLineBreak();
            AppendLineBreak();
        }

        private void ContentBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Close();
                return;
            }

            if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None && TryExecuteActionAtCaret())
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void ContentBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
            {
                return;
            }

            var index = _contentBox.GetCharIndexFromPosition(e.Location);
            _contentBox.SelectionStart = index;
            _contentBox.SelectionLength = 0;
        }

        private void ContentBox_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                TryExecuteActionAtCaret();
            }
        }

        private void ContentBox_MouseMove(object sender, MouseEventArgs e)
        {
            _contentBox.Cursor = FindInteractiveSpan(_contentBox.GetCharIndexFromPosition(e.Location)) != null
                ? Cursors.Hand
                : Cursors.IBeam;
        }

        private void ContextMenu_Popup(object sender, EventArgs e)
        {
            _currentInteractiveSpan = FindCurrentInteractiveSpan() ?? FindSingleInteractiveSpanInCurrentLine();
            _currentCodeBlockSpan = FindCurrentCodeBlockSpan();
            _contextMenu.MenuItems.Clear();

            if (_currentInteractiveSpan != null)
            {
                if (_currentInteractiveSpan.Kind == InteractiveKind.Link)
                {
                    _contextMenu.MenuItems.Add(_openLinkMenuItem);
                    _contextMenu.MenuItems.Add(_copyLinkMenuItem);
                }
                else
                {
                    _contextMenu.MenuItems.Add(_copyCodeMenuItem);
                }
            }
            else if (_currentCodeBlockSpan != null)
            {
                _contextMenu.MenuItems.Add(_copyCodeMenuItem);
            }

            if (_contentBox.SelectionLength > 0)
            {
                if (_contextMenu.MenuItems.Count > 0)
                {
                    _contextMenu.MenuItems.Add("-");
                }

                _contextMenu.MenuItems.Add(_copySelectionMenuItem);
            }

            if (_contextMenu.MenuItems.Count > 0)
            {
                _contextMenu.MenuItems.Add("-");
            }

            _contextMenu.MenuItems.Add(_closeMenuItem);
        }

        private void OpenLinkMenuItem_Click(object sender, EventArgs e)
        {
            var span = _currentInteractiveSpan ?? FindCurrentInteractiveSpan();
            if (span != null && span.Kind == InteractiveKind.Link)
            {
                OpenExternalUri(span.Payload);
            }
        }

        private void CopyLinkMenuItem_Click(object sender, EventArgs e)
        {
            var span = _currentInteractiveSpan ?? FindCurrentInteractiveSpan();
            if (span != null && span.Kind == InteractiveKind.Link)
            {
                CopyTextToClipboard(span.Payload);
            }
        }

        private void CopyCodeMenuItem_Click(object sender, EventArgs e)
        {
            var span = _currentInteractiveSpan ?? FindCurrentInteractiveSpan() ?? FindSingleInteractiveSpanInCurrentLine();
            if (span != null && span.Kind == InteractiveKind.CopyCode)
            {
                CopyTextToClipboard(span.Payload);
                return;
            }

            var codeBlock = _currentCodeBlockSpan ?? FindCurrentCodeBlockSpan();
            if (codeBlock != null)
            {
                CopyTextToClipboard(codeBlock.CodeText);
            }
        }

        private void CopySelectionMenuItem_Click(object sender, EventArgs e)
        {
            if (_contentBox.SelectionLength > 0)
            {
                CopyTextToClipboard(_contentBox.SelectedText);
            }
        }

        private void CloseMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private bool TryExecuteActionAtCaret()
        {
            var span = FindCurrentInteractiveSpan() ?? FindSingleInteractiveSpanInCurrentLine();
            if (span != null)
            {
                if (span.Kind == InteractiveKind.Link)
                {
                    OpenExternalUri(span.Payload);
                }
                else
                {
                    CopyTextToClipboard(span.Payload);
                }

                return true;
            }

            var codeBlock = FindCurrentCodeBlockSpan();
            if (codeBlock == null)
            {
                return false;
            }

            CopyTextToClipboard(codeBlock.CodeText);
            return true;
        }

        private InteractiveSpan FindCurrentInteractiveSpan()
        {
            var index = _contentBox.SelectionStart;
            return FindInteractiveSpan(index) ?? FindInteractiveSpan(Math.Max(0, index - 1));
        }

        private CodeBlockSpan FindCurrentCodeBlockSpan()
        {
            var index = _contentBox.SelectionStart;
            return FindCodeBlockSpan(index) ?? FindCodeBlockSpan(Math.Max(0, index - 1));
        }

        private InteractiveSpan FindSingleInteractiveSpanInCurrentLine()
        {
            var lineIndex = _contentBox.GetLineFromCharIndex(_contentBox.SelectionStart);
            if (lineIndex < 0)
            {
                return null;
            }

            var lineStart = _contentBox.GetFirstCharIndexFromLine(lineIndex);
            if (lineStart < 0)
            {
                return null;
            }

            var lines = _contentBox.Lines;
            if (lineIndex >= lines.Length)
            {
                return null;
            }

            var lineLength = (lines[lineIndex] ?? string.Empty).Length;
            var lineEnd = lineStart + lineLength;
            InteractiveSpan onlySpan = null;
            for (var i = 0; i < _interactiveSpans.Count; i++)
            {
                var span = _interactiveSpans[i];
                if (span.Start >= lineEnd || span.Start + span.Length <= lineStart)
                {
                    continue;
                }

                if (onlySpan != null)
                {
                    return null;
                }

                onlySpan = span;
            }

            return onlySpan;
        }

        private InteractiveSpan FindInteractiveSpan(int index)
        {
            for (var i = 0; i < _interactiveSpans.Count; i++)
            {
                if (_interactiveSpans[i].Contains(index))
                {
                    return _interactiveSpans[i];
                }
            }

            return null;
        }

        private CodeBlockSpan FindCodeBlockSpan(int index)
        {
            for (var i = 0; i < _codeBlockSpans.Count; i++)
            {
                if (_codeBlockSpans[i].Contains(index))
                {
                    return _codeBlockSpans[i];
                }
            }

            return null;
        }

        private static InlineToken FindNextInlineToken(string text, int startIndex)
        {
            Match bestMatch = null;
            var bestKind = InlineTokenKind.MarkdownLink;

            var match = MarkdownLinkRegex.Match(text, startIndex);
            if (match.Success)
            {
                bestMatch = match;
            }

            match = RawUrlRegex.Match(text, startIndex);
            if (match.Success && (bestMatch == null || match.Index < bestMatch.Index))
            {
                bestMatch = match;
                bestKind = InlineTokenKind.RawUrl;
            }

            match = InlineCodeRegex.Match(text, startIndex);
            if (match.Success && (bestMatch == null || match.Index < bestMatch.Index))
            {
                bestMatch = match;
                bestKind = InlineTokenKind.InlineCode;
            }

            if (bestMatch == null)
            {
                return null;
            }

            if (bestKind == InlineTokenKind.MarkdownLink)
            {
                return new InlineToken
                {
                    Index = bestMatch.Index,
                    Length = bestMatch.Length,
                    Kind = bestKind,
                    DisplayText = bestMatch.Groups["text"].Value,
                    Value = bestMatch.Groups["url"].Value
                };
            }

            if (bestKind == InlineTokenKind.RawUrl)
            {
                var url = TrimTrailingUrlPunctuation(bestMatch.Groups["url"].Value);
                return new InlineToken
                {
                    Index = bestMatch.Index,
                    Length = url.Length,
                    Kind = bestKind,
                    DisplayText = url,
                    Value = url
                };
            }

            return new InlineToken
            {
                Index = bestMatch.Index,
                Length = bestMatch.Length,
                Kind = bestKind,
                DisplayText = bestMatch.Groups["code"].Value,
                Value = bestMatch.Groups["code"].Value
            };
        }

        private static string TrimTrailingUrlPunctuation(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            var end = url.Length;
            while (end > 0)
            {
                var ch = url[end - 1];
                if (ch != '.' && ch != ',' && ch != ';' && ch != ':' && ch != '!' && ch != '?')
                {
                    break;
                }

                end--;
            }

            return end == url.Length ? url : url.Substring(0, end);
        }

        private static string GetViewerText(ChatHistoryItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(item.MarkdownText))
            {
                return item.MarkdownText;
            }

            if (!string.IsNullOrWhiteSpace(item.FullText))
            {
                return item.FullText;
            }

            return item.DisplayText ?? string.Empty;
        }

        private static string NormalizeForViewer(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static void OpenExternalUri(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
            }
            catch
            {
            }
        }

        private static void CopyTextToClipboard(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
            }
        }

        private enum InteractiveKind
        {
            Link = 1,
            CopyCode = 2
        }

        private enum InlineTokenKind
        {
            MarkdownLink = 1,
            RawUrl = 2,
            InlineCode = 3
        }

        private sealed class InteractiveSpan
        {
            public int Start;
            public int Length;
            public InteractiveKind Kind;
            public string Payload;

            public bool Contains(int index)
            {
                return index >= Start && index < Start + Length;
            }
        }

        private sealed class CodeBlockSpan
        {
            public int Start;
            public int Length;
            public string CodeText;

            public bool Contains(int index)
            {
                return index >= Start && index < Start + Length;
            }
        }

        private sealed class InlineToken
        {
            public int Index;
            public int Length;
            public InlineTokenKind Kind;
            public string DisplayText;
            public string Value;
        }

        internal static class MessageViewerDiagnostics
        {
            private static readonly object SyncRoot = new object();
            private static readonly string LogFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyCodex",
                "viewer-diagnostics.log");

            public static void LogException(string message, Exception ex)
            {
                var builder = new StringBuilder();
                builder.AppendLine(message ?? string.Empty);
                if (ex != null)
                {
                    builder.AppendLine(ex.ToString());
                }

                WriteEntry("ERROR", builder.ToString().TrimEnd());
            }

            private static void WriteEntry(string level, string message)
            {
                try
                {
                    var directory = Path.GetDirectoryName(LogFilePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var entry = string.Format(
                        CultureInfo.InvariantCulture,
                        "[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] {2}{3}",
                        DateTime.Now,
                        level ?? "INFO",
                        message ?? string.Empty,
                        Environment.NewLine);

                    lock (SyncRoot)
                    {
                        File.AppendAllText(LogFilePath, entry, Encoding.UTF8);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
