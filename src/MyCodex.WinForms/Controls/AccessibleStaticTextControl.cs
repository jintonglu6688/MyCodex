using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyCodex.WinForms.Controls
{
    internal sealed class AccessibleStaticTextControl : Control
    {
        public AccessibleStaticTextControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            AccessibleRole = AccessibleRole.StaticText;
            TabStop = true;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width > 0 ? proposedSize.Width : Math.Max(Width, 1);
            var measured = TextRenderer.MeasureText(
                Text ?? string.Empty,
                Font,
                new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.Left);
            return new Size(width, measured.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var bounds = new Rectangle(0, 0, Width, Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                bounds,
                ForeColor,
                BackColor,
                TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            if (Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, bounds);
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }
}
