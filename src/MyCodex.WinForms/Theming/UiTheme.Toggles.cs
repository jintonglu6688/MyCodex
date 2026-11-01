using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal static partial class UiTheme
    {
        internal static bool PaintToggleControl(ButtonBase control, PaintEventArgs e)
        {
            if (control == null || control.Width <= 0 || control.Height <= 0)
            {
                return false;
            }

            ToggleThemeState state;
            if (_currentMode == UiThemeMode.HighContrast ||
                !ToggleStates.TryGetValue(control, out state) ||
                control is CheckBox && ((CheckBox)control).Appearance == Appearance.Button ||
                control is RadioButton && ((RadioButton)control).Appearance == Appearance.Button)
            {
                return false;
            }

            var background = !control.Enabled
                ? SubtleBackColor
                : state.Hovered ? AccentSoftBackColor : control.BackColor;
            var foreground = control.Enabled ? TextColor : MutedTextColor;
            using (var backgroundBrush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(backgroundBrush, control.ClientRectangle);
            }

            var glyphSize = Scale(16);
            var gap = Scale(6);
            var horizontalPadding = Scale(4);
            var textSize = TextRenderer.MeasureText(
                e.Graphics,
                control.Text,
                control.Font,
                Size.Empty,
                TextFormatFlags.SingleLine);
            var totalWidth = glyphSize + gap + textSize.Width;
            var alignRight = control.TextAlign == ContentAlignment.MiddleRight ||
                control.TextAlign == ContentAlignment.TopRight ||
                control.TextAlign == ContentAlignment.BottomRight;
            var alignCenter = control.TextAlign == ContentAlignment.TopCenter ||
                control.TextAlign == ContentAlignment.MiddleCenter ||
                control.TextAlign == ContentAlignment.BottomCenter;
            var startX = alignRight
                ? Math.Max(horizontalPadding, control.ClientSize.Width - totalWidth - horizontalPadding)
                : alignCenter
                    ? Math.Max(horizontalPadding, (control.ClientSize.Width - totalWidth) / 2)
                    : horizontalPadding;
            var glyphBounds = new Rectangle(
                startX,
                Math.Max(1, (control.ClientSize.Height - glyphSize) / 2),
                glyphSize,
                glyphSize);
            DrawToggleGlyph(e.Graphics, control, state, glyphBounds, foreground);

            var textBounds = new Rectangle(
                glyphBounds.Right + gap,
                0,
                Math.Max(0, control.ClientSize.Width - glyphBounds.Right - gap - horizontalPadding),
                control.ClientSize.Height);
            TextRenderer.DrawText(
                e.Graphics,
                control.Text,
                control.Font,
                textBounds,
                foreground,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis);
            if (control.Focused)
            {
                ControlPaint.DrawFocusRectangle(
                    e.Graphics,
                    Rectangle.Inflate(glyphBounds, Scale(1), Scale(1)),
                    foreground,
                    background);
            }

            return true;
        }

        private static void DrawToggleGlyph(
            Graphics graphics,
            ButtonBase control,
            ToggleThemeState state,
            Rectangle bounds,
            Color foreground)
        {
            var checkedState = control is CheckBox && ((CheckBox)control).CheckState == CheckState.Checked ||
                control is RadioButton && ((RadioButton)control).Checked;
            var indeterminate = control is CheckBox && ((CheckBox)control).CheckState == CheckState.Indeterminate;
            var borderColor = control.Focused || state.Hovered ? ActiveBorderColor : BorderColor;
            using (var fillBrush = new SolidBrush(SubtleBackColor))
            using (var borderPen = new Pen(borderColor, Math.Max(1F, (float)Scale(1))))
            using (var markPen = new Pen(control.Enabled ? ActiveBorderColor : MutedTextColor, Math.Max(1.5F, (float)Scale(2))))
            {
                if (state.Radio)
                {
                    graphics.FillEllipse(fillBrush, bounds);
                    graphics.DrawEllipse(borderPen, bounds);
                    if (checkedState)
                    {
                        var inner = Rectangle.Inflate(bounds, -Scale(4), -Scale(4));
                        using (var markBrush = new SolidBrush(control.Enabled ? AccentColor : MutedTextColor))
                        {
                            graphics.FillEllipse(markBrush, inner);
                        }
                    }
                }
                else
                {
                    graphics.FillRectangle(fillBrush, bounds);
                    graphics.DrawRectangle(borderPen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
                    if (checkedState)
                    {
                        graphics.DrawLines(markPen, new[]
                        {
                            new Point(bounds.Left + Scale(3), bounds.Top + bounds.Height / 2),
                            new Point(bounds.Left + bounds.Width / 2, bounds.Bottom - Scale(4)),
                            new Point(bounds.Right - Scale(3), bounds.Top + Scale(3))
                        });
                    }
                    else if (indeterminate)
                    {
                        var middleY = bounds.Top + bounds.Height / 2;
                        graphics.DrawLine(markPen, bounds.Left + Scale(4), middleY, bounds.Right - Scale(4), middleY);
                    }
                }
            }
        }
    }
}
