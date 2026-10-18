using System;
using System.Text;

namespace MyCodex.WinForms.Forms
{
    internal sealed class VisibleLogBuffer
    {
        private readonly int _maxVisibleCharacters;
        private readonly int _targetVisibleCharacters;
        private readonly StringBuilder _visibleBuffer;
        private readonly StringBuilder _pendingBuffer;
        private bool _endsWithNewLine;

        public VisibleLogBuffer(int maxVisibleCharacters, int targetVisibleCharacters)
        {
            _maxVisibleCharacters = Math.Max(1, maxVisibleCharacters);
            _targetVisibleCharacters = Math.Max(1, Math.Min(_maxVisibleCharacters, targetVisibleCharacters));
            _visibleBuffer = new StringBuilder();
            _pendingBuffer = new StringBuilder();
            _endsWithNewLine = true;
        }

        internal bool HasPendingText
        {
            get { return _pendingBuffer.Length > 0; }
        }

        internal bool HasContent
        {
            get { return _visibleBuffer.Length > 0 || _pendingBuffer.Length > 0; }
        }

        internal void AppendLogLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var builder = new StringBuilder();
            if (HasContent && !_endsWithNewLine)
            {
                builder.AppendLine();
            }

            builder.Append(text);
            builder.Append(Environment.NewLine);
            QueueText(builder.ToString());
        }

        internal void QueueText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (text.Length > _maxVisibleCharacters)
            {
                text = text.Substring(text.Length - _targetVisibleCharacters);
            }

            _pendingBuffer.Append(text);
            if (_pendingBuffer.Length > _maxVisibleCharacters)
            {
                _pendingBuffer.Remove(0, _pendingBuffer.Length - _targetVisibleCharacters);
            }

            _endsWithNewLine = EndsWithNewLine(text);
        }

        internal string FlushPendingText()
        {
            if (_pendingBuffer.Length == 0)
            {
                return null;
            }

            _visibleBuffer.Append(_pendingBuffer.ToString());
            _pendingBuffer.Length = 0;

            if (_visibleBuffer.Length > _maxVisibleCharacters)
            {
                _visibleBuffer.Remove(0, _visibleBuffer.Length - _targetVisibleCharacters);
            }

            return _visibleBuffer.ToString();
        }

        internal string GetSnapshotText()
        {
            if (_visibleBuffer.Length == 0)
            {
                return _pendingBuffer.Length == 0 ? string.Empty : _pendingBuffer.ToString();
            }

            if (_pendingBuffer.Length == 0)
            {
                return _visibleBuffer.ToString();
            }

            return _visibleBuffer.ToString() + _pendingBuffer.ToString();
        }

        internal void Clear()
        {
            _pendingBuffer.Length = 0;
            _visibleBuffer.Length = 0;
            _endsWithNewLine = true;
        }

        private static bool EndsWithNewLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var lastChar = text[text.Length - 1];
            return lastChar == '\r' || lastChar == '\n';
        }
    }
}
