using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Controls
{
    /// <summary>
    /// Custom TextBox with enhanced focus border effect
    /// </summary>
    public class ThemedTextBox : TextBox
    {
        private const int WmPaste = 0x0302;
        private bool _isFocused;
        private bool _isNormalizingText;

        public ThemedTextBox()
        {
            BorderStyle = BorderStyle.FixedSingle;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            _isFocused = true;
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _isFocused = false;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            if (!_isNormalizingText && Multiline)
            {
                try
                {
                    _isNormalizingText = true;
                    if (TextBoxLineEndingNormalizer.NormalizeTextBox(this))
                    {
                        return;
                    }
                }
                finally
                {
                    _isNormalizingText = false;
                }
            }

            base.OnTextChanged(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmPaste && Multiline && !ReadOnly && TryHandleNormalizedMultilinePaste())
            {
                return;
            }

            base.WndProc(ref m);

            // WM_PAINT = 0x000F, WM_NCPAINT = 0x0085
            if (m.Msg == 0x000F || m.Msg == 0x0085)
            {
                if (_isFocused)
                {
                    DrawFocusBorder();
                }
            }
        }

        internal static string NormalizePastedMultilineText(string text)
        {
            return TextBoxLineEndingNormalizer.NormalizeMultilineText(text);
        }

        private bool TryHandleNormalizedMultilinePaste()
        {
            string clipboardText = null;
            try
            {
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    clipboardText = Clipboard.GetText(TextDataFormat.UnicodeText);
                }
                else if (Clipboard.ContainsText())
                {
                    clipboardText = Clipboard.GetText();
                }
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrEmpty(clipboardText))
            {
                return false;
            }

            var normalizedText = NormalizePastedMultilineText(clipboardText);
            if (!string.Equals(normalizedText, clipboardText, StringComparison.Ordinal))
            {
                SelectedText = normalizedText;
                return true;
            }

            return false;
        }

        private void DrawFocusBorder()
        {
            using (var g = Graphics.FromHwnd(Handle))
            {
                var rect = new Rectangle(0, 0, Width - 1, Height - 1);

                // Draw 2px focus border
                using (var pen = new Pen(UiTheme.ActiveBorderColor, 2))
                {
                    g.DrawRectangle(pen, rect);
                }
            }
        }
    }
}
