using System;
using System.Collections.Generic;
using System.Linq;

namespace MyCodex.WinForms.Forms
{
    internal enum CommandTerminalEntryKind
    {
        System = 0,
        StandardOutput = 1,
        StandardError = 2,
        Interaction = 3,
        Input = 4
    }

    internal sealed class CommandTerminalEntry
    {
        public CommandTerminalEntry(CommandTerminalEntryKind kind, string text)
        {
            Kind = kind;
            Text = text ?? string.Empty;
        }

        public CommandTerminalEntryKind Kind { get; }

        public string Text { get; }
    }

    internal sealed class CommandTerminalBuffer
    {
        private readonly List<CommandTerminalEntry> _entries;
        private readonly int _maxCharacters;
        private int _totalCharacters;

        public CommandTerminalBuffer(int maxCharacters)
        {
            _entries = new List<CommandTerminalEntry>();
            _maxCharacters = Math.Max(1, maxCharacters);
        }

        public void Append(CommandTerminalEntryKind kind, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var normalizedText = NormalizeText(text);
            _entries.Add(new CommandTerminalEntry(kind, normalizedText));
            _totalCharacters += normalizedText.Length;
            TrimToLimit();
        }

        public void Clear()
        {
            _entries.Clear();
            _totalCharacters = 0;
        }

        public CommandTerminalEntry[] GetEntries()
        {
            return _entries
                .Select(entry => new CommandTerminalEntry(entry.Kind, entry.Text))
                .ToArray();
        }

        public string GetSnapshotText()
        {
            return string.Join(Environment.NewLine, _entries.Select(entry => entry.Text).Where(text => text.Length > 0));
        }

        public string[] GetReadableLines()
        {
            return GetSnapshotText()
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.TrimEnd())
                .ToArray();
        }

        private void TrimToLimit()
        {
            while (_totalCharacters > _maxCharacters && _entries.Count > 1)
            {
                var first = _entries[0];
                if (_totalCharacters - first.Text.Length < _maxCharacters)
                {
                    break;
                }

                _entries.RemoveAt(0);
                _totalCharacters -= first.Text.Length;
            }

            if (_totalCharacters <= _maxCharacters || _entries.Count == 0)
            {
                return;
            }

            var overflow = _totalCharacters - _maxCharacters;
            var entry = _entries[0];
            if (overflow >= entry.Text.Length)
            {
                _entries.RemoveAt(0);
                _totalCharacters -= entry.Text.Length;
                return;
            }

            _entries[0] = new CommandTerminalEntry(entry.Kind, entry.Text.Substring(overflow));
            _totalCharacters -= overflow;
        }

        private static string NormalizeText(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .TrimEnd('\n');
        }
    }
}
