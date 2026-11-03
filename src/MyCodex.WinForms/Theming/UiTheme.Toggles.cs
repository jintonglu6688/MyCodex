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
            if (!UsesThemedToggleLayout(control) || !ToggleStates.TryGetValue(control, out state))
            {
                return false;
            }

            var background = control.Enabled && state.Hovered ? AccentSoftBackColor : control.BackColor;
            var foreground = control.Enabled ? TextColor : DisabledTextColor;
            using (var backgroundBrush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(backgroundBrush, control.ClientRectangle);
            }

            var glyphSize = Scale(16);
            var gap = Scale(6);
            var horizontalPadding = Scale(4);
            var flags = GetToggleTextFlags(control);
            var textSize = TextRenderer.MeasureText(e.Graphics, control.Text, control.Font, Size.Empty, flags);
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

            var textLeft = glyphBounds.Right + gap;
            var textWidth = Math.Max(0, control.ClientSize.Width - textLeft - horizontalPadding);
            if (textSize.Width <= textWidth)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    control.Text,
                    control.Font,
                    new Rectangle(textLeft, 0, textWidth, control.ClientSize.Height),
                    foreground,
                    flags | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            else
            {
                // Text wider than the control wraps, as the native control does.
                var wrapFlags = (flags & ~TextFormatFlags.SingleLine) | TextFormatFlags.WordBreak | TextFormatFlags.Left;
                var wrapped = TextRenderer.MeasureText(e.Graphics, control.Text, control.Font, new Size(textWidth, int.MaxValue), wrapFlags);
                var top = Math.Max(0, (control.ClientSize.Height - wrapped.Height) / 2);
                TextRenderer.DrawText(
                    e.Graphics,
                    control.Text,
                    control.Font,
                    new Rectangle(textLeft, top, textWidth, control.ClientSize.Height - top),
                    foreground,
                    wrapFlags | TextFormatFlags.EndEllipsis);
            }

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

        // Keeps AutoSize toggles as wide as the themed glyph and text drawn by PaintToggleControl.
        internal static Size GetTogglePreferredSize(ButtonBase control, Size nativeSize)
        {
            if (control == null || !UsesThemedToggleLayout(control) || string.IsNullOrEmpty(control.Text))
            {
                return nativeSize;
            }

            var text = TextRenderer.MeasureText(control.Text, control.Font, Size.Empty, GetToggleTextFlags(control));
            var width = Scale(4) + Scale(16) + Scale(6) + text.Width + Scale(4) + control.Padding.Horizontal;
            var height = Math.Max(text.Height, Scale(16)) + Scale(2) + control.Padding.Vertical;
            return new Size(Math.Max(nativeSize.Width, width), Math.Max(nativeSize.Height, height));
        }

        private static bool UsesThemedToggleLayout(ButtonBase control)
        {
            return _currentMode != UiThemeMode.HighContrast &&
                !(control is CheckBox && ((CheckBox)control).Appearance == Appearance.Button) &&
                !(control is RadioButton && ((RadioButton)control).Appearance == Appearance.Button);
        }

        private static TextFormatFlags GetToggleTextFlags(ButtonBase control)
        {
            return control.UseMnemonic
                ? TextFormatFlags.SingleLine
                : TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
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
            var borderColor = !control.Enabled
                ? DisabledTextColor
                : control.Focused || state.Hovered ? ActiveBorderColor : BorderColor;
            var markColor = control.Enabled ? ActiveBorderColor : DisabledTextColor;
            using (var fillBrush = new SolidBrush(SubtleBackColor))
            using (var borderPen = new Pen(borderColor, Math.Max(1F, (float)Scale(1))))
            using (var markPen = new Pen(markColor, Math.Max(1.5F, (float)Scale(2))))
            {
                if (state.Radio)
                {
                    graphics.FillEllipse(fillBrush, bounds);
                    graphics.DrawEllipse(borderPen, bounds);
                    if (checkedState)
                    {
                        var inner = Rectangle.Inflate(bounds, -Scale(4), -Scale(4));
                        using (var markBrush = new SolidBrush(control.Enabled ? AccentColor : DisabledTextColor))
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
