using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal sealed class ThemedDateTimePicker : DateTimePicker
    {
        private const int WindowPaint = 0x000F;
        private const int WindowPrint = 0x0317;
        private const int WindowPrintClient = 0x0318;

        public ThemedDateTimePicker()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WindowPaint && IsHandleCreated && Visible)
            {
                using (var graphics = Graphics.FromHwnd(Handle))
                {
                    PaintChrome(graphics);
                }
            }
            else if ((message.Msg == WindowPrint || message.Msg == WindowPrintClient) &&
                     message.WParam != IntPtr.Zero)
            {
                using (var graphics = Graphics.FromHdc(message.WParam))
                {
                    PaintChrome(graphics);
                }
            }
        }

        private void PaintChrome(Graphics graphics)
        {
            if (UiTheme.CurrentMode == UiThemeMode.HighContrast || Width <= 1 || Height <= 1)
            {
                return;
            }

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            var buttonWidth = UiTheme.Scale(24);
            var buttonBounds = new Rectangle(
                Math.Max(1, bounds.Right - buttonWidth),
                bounds.Top,
                Math.Min(buttonWidth, Math.Max(0, bounds.Width - 1)),
                bounds.Height);
            var fieldBackColor = Enabled ? UiTheme.CardBackColor : UiTheme.SubtleBackColor;
            var buttonBackColor = Focused ? UiTheme.AccentSoftBackColor : fieldBackColor;
            var textColor = Enabled ? UiTheme.TextColor : UiTheme.MutedTextColor;

            using (var fieldBrush = new SolidBrush(fieldBackColor))
            using (var buttonBrush = new SolidBrush(buttonBackColor))
            using (var borderPen = new Pen(Focused ? UiTheme.ActiveBorderColor : UiTheme.BorderColor))
            {
                graphics.FillRectangle(fieldBrush, bounds);
                graphics.FillRectangle(buttonBrush, buttonBounds);
                graphics.DrawLine(borderPen, buttonBounds.Left, buttonBounds.Top + 2, buttonBounds.Left, buttonBounds.Bottom - 2);
                graphics.DrawRectangle(borderPen, bounds);
            }

            var textBounds = new Rectangle(
                bounds.Left + UiTheme.Scale(6),
                bounds.Top,
                Math.Max(0, buttonBounds.Left - UiTheme.Scale(10)),
                bounds.Height);
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                textBounds,
                textColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            var centerX = buttonBounds.Left + buttonBounds.Width / 2;
            var centerY = buttonBounds.Top + buttonBounds.Height / 2;
            using (var arrowPen = new Pen(textColor, Math.Max(1.4F, (float)UiTheme.Scale(2))))
            {
                if (ShowUpDown)
                {
                    graphics.DrawLines(arrowPen, new[]
                    {
                        new Point(centerX - UiTheme.Scale(3), centerY - UiTheme.Scale(3)),
                        new Point(centerX, centerY - UiTheme.Scale(6)),
                        new Point(centerX + UiTheme.Scale(3), centerY - UiTheme.Scale(3))
                    });
                    graphics.DrawLines(arrowPen, new[]
                    {
                        new Point(centerX - UiTheme.Scale(3), centerY + UiTheme.Scale(3)),
                        new Point(centerX, centerY + UiTheme.Scale(6)),
                        new Point(centerX + UiTheme.Scale(3), centerY + UiTheme.Scale(3))
                    });
                }
                else
                {
                    graphics.DrawLines(arrowPen, new[]
                    {
                        new Point(centerX - UiTheme.Scale(4), centerY - UiTheme.Scale(2)),
                        new Point(centerX, centerY + UiTheme.Scale(2)),
                        new Point(centerX + UiTheme.Scale(4), centerY - UiTheme.Scale(2))
                    });
                }
            }
        }
    }
}
