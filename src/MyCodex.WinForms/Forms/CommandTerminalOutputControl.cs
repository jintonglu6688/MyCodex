using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class CommandTerminalOutputControl : Control
    {
        private readonly List<CommandTerminalEntry> _entries;
        private readonly List<VisualLine> _visualLines;

        public CommandTerminalOutputControl()
        {
            _entries = new List<CommandTerminalEntry>();
            _visualLines = new List<VisualLine>();
            Font = UiTheme.CreateCodeFont(UiTheme.TitleFontSize, FontStyle.Regular);
            DoubleBuffered = true;
            TabStop = false;
            ApplyTheme();
        }

        public void SetEntries(IEnumerable<CommandTerminalEntry> entries)
        {
            _entries.Clear();
            if (entries != null)
            {
                _entries.AddRange(entries.Where(entry => entry != null));
            }

            RebuildVisualLines();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);

            if (_visualLines.Count <= 0)
            {
                return;
            }

            using (var stdoutBrush = new SolidBrush(GetEntryColor(CommandTerminalEntryKind.StandardOutput)))
            using (var stderrBrush = new SolidBrush(GetEntryColor(CommandTerminalEntryKind.StandardError)))
            using (var systemBrush = new SolidBrush(GetEntryColor(CommandTerminalEntryKind.System)))
            using (var interactionBrush = new SolidBrush(GetEntryColor(CommandTerminalEntryKind.Interaction)))
            using (var inputBrush = new SolidBrush(GetEntryColor(CommandTerminalEntryKind.Input)))
            {
                var lineHeight = Math.Max(UiTheme.Scale(16), Font.Height + UiTheme.Scale(3));
                var maxLines = Math.Max(1, (ClientSize.Height - UiTheme.Scale(16)) / lineHeight);
                var start = Math.Max(0, _visualLines.Count - maxLines);
                var y = UiTheme.Scale(8);

                for (var i = start; i < _visualLines.Count; i++)
                {
                    var line = _visualLines[i];
                    var brush = GetBrush(line.Kind, stdoutBrush, stderrBrush, systemBrush, interactionBrush, inputBrush);
                    e.Graphics.DrawString(line.Text, Font, brush, new PointF(UiTheme.Scale(10), y));
                    y += lineHeight;
                }
            }
        }

        internal void ApplyTheme()
        {
            BackColor = GetTerminalBackColor();
            ForeColor = GetTerminalDefaultTextColor();
            Invalidate();
        }

        private void RebuildVisualLines()
        {
            _visualLines.Clear();
            foreach (var entry in _entries)
            {
                var prefix = GetPrefix(entry.Kind);
                var lines = (entry.Text ?? string.Empty)
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split(new[] { '\n' }, StringSplitOptions.None);

                foreach (var line in lines)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    _visualLines.Add(new VisualLine(entry.Kind, prefix + line));
                }
            }
        }

        private static Brush GetBrush(
            CommandTerminalEntryKind kind,
            Brush stdoutBrush,
            Brush stderrBrush,
            Brush systemBrush,
            Brush interactionBrush,
            Brush inputBrush)
        {
            switch (kind)
            {
                case CommandTerminalEntryKind.StandardError:
                    return stderrBrush;
                case CommandTerminalEntryKind.System:
                    return systemBrush;
                case CommandTerminalEntryKind.Interaction:
                    return interactionBrush;
                case CommandTerminalEntryKind.Input:
                    return inputBrush;
                default:
                    return stdoutBrush;
            }
        }

        private static string GetPrefix(CommandTerminalEntryKind kind)
        {
            switch (kind)
            {
                case CommandTerminalEntryKind.StandardError:
                    return "[stderr] ";
                case CommandTerminalEntryKind.System:
                    return "$ ";
                case CommandTerminalEntryKind.Interaction:
                    return "? ";
                case CommandTerminalEntryKind.Input:
                    return "> ";
                default:
                    return string.Empty;
            }
        }

        private static Color GetEntryColor(CommandTerminalEntryKind kind)
        {
            if (UiTheme.CurrentMode == UiThemeMode.HighContrast)
            {
                switch (kind)
                {
                    case CommandTerminalEntryKind.StandardError:
                        return SystemColors.ControlText;
                    case CommandTerminalEntryKind.System:
                        return SystemColors.GrayText;
                    case CommandTerminalEntryKind.Interaction:
                        return SystemColors.ControlText;
                    case CommandTerminalEntryKind.Input:
                        return SystemColors.ControlText;
                    default:
                        return SystemColors.ControlText;
                }
            }

            switch (kind)
            {
                case CommandTerminalEntryKind.StandardError:
                    return Color.FromArgb(248, 113, 113);
                case CommandTerminalEntryKind.System:
                    return Color.FromArgb(148, 163, 184);
                case CommandTerminalEntryKind.Interaction:
                    return Color.FromArgb(250, 204, 21);
                case CommandTerminalEntryKind.Input:
                    return Color.FromArgb(125, 211, 252);
                default:
                    return GetTerminalDefaultTextColor();
            }
        }

        private static Color GetTerminalBackColor()
        {
            return UiTheme.CurrentMode == UiThemeMode.HighContrast
                ? UiTheme.CardBackColor
                : Color.FromArgb(11, 18, 32);
        }

        private static Color GetTerminalDefaultTextColor()
        {
            return UiTheme.CurrentMode == UiThemeMode.HighContrast
                ? UiTheme.TextColor
                : Color.FromArgb(226, 232, 240);
        }

        private sealed class VisualLine
        {
            public VisualLine(CommandTerminalEntryKind kind, string text)
            {
                Kind = kind;
                Text = text;
            }

            public CommandTerminalEntryKind Kind { get; }

            public string Text { get; }
        }
    }
}
