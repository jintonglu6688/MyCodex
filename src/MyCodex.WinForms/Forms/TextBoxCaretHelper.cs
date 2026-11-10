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
