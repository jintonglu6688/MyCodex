using System.Windows.Forms;

namespace MyCodex.WinForms.Forms
{
    internal static class TextBoxCaretHelper
    {
        internal static void SetTextAndMoveToStart(TextBoxBase textBox, string text)
        {
            if (textBox == null || textBox.IsDisposed)
            {
                return;
            }

            textBox.Text = text ?? string.Empty;
            MoveToStart(textBox);
        }

        internal static void SetTextAndRestoreSelection(TextBoxBase textBox, string text, int selectionStart, int selectionLength)
        {
            if (textBox == null || textBox.IsDisposed)
            {
                return;
            }

            textBox.Text = text ?? string.Empty;
            var safeSelectionStart = System.Math.Max(0, System.Math.Min(selectionStart, textBox.TextLength));
            var safeSelectionLength = System.Math.Max(0, System.Math.Min(selectionLength, textBox.TextLength - safeSelectionStart));
            textBox.SelectionStart = safeSelectionStart;
            textBox.SelectionLength = safeSelectionLength;
            textBox.ScrollToCaret();
        }

        internal static void MoveToStart(TextBoxBase textBox)
        {
            if (textBox == null || textBox.IsDisposed)
            {
                return;
            }

            textBox.SelectionStart = 0;
            textBox.SelectionLength = 0;
            textBox.ScrollToCaret();
        }
    }
}
