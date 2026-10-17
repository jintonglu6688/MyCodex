using System;
using System.Windows.Forms;

namespace MyCodex.WinForms.Controls
{
    internal static class TextBoxLineEndingNormalizer
    {
        public static string NormalizeMultilineText(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Replace("\n", Environment.NewLine);
        }

        public static bool NormalizeTextBox(TextBox textBox)
        {
            if (textBox == null || !textBox.Multiline)
            {
                return false;
            }

            var currentText = textBox.Text;
            var normalizedText = NormalizeMultilineText(currentText);
            if (string.Equals(currentText, normalizedText, StringComparison.Ordinal))
            {
                return false;
            }

            var selectionStart = textBox.SelectionStart;
            var selectionEnd = selectionStart + textBox.SelectionLength;
            textBox.Text = normalizedText;
            var normalizedStart = MapSelectionIndex(currentText, selectionStart, normalizedText.Length);
            var normalizedEnd = MapSelectionIndex(currentText, selectionEnd, normalizedText.Length);
            textBox.SelectionStart = normalizedStart;
            textBox.SelectionLength = Math.Max(0, normalizedEnd - normalizedStart);
            return true;
        }

        private static int MapSelectionIndex(string text, int index, int normalizedLength)
        {
            if (string.IsNullOrEmpty(text) || index <= 0)
            {
                return 0;
            }

            var normalizedIndex = 0;
            var limit = Math.Min(index, text.Length);
            for (var i = 0; i < limit; i++)
            {
                var ch = text[i];
                if (ch == '\r')
                {
                    if (i + 1 < limit && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    normalizedIndex += Environment.NewLine.Length;
                }
                else if (ch == '\n')
                {
                    normalizedIndex += Environment.NewLine.Length;
                }
                else
                {
                    normalizedIndex++;
                }
            }

            return Math.Min(normalizedIndex, normalizedLength);
        }
    }
}
