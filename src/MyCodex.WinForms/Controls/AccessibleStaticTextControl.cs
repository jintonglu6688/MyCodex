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
            // Overriding the public method skips Control's own MinimumSize/MaximumSize clamp, so apply it here;
            // otherwise layout sizes a row for the text alone while the control stays at its minimum height.
            var width = proposedSize.Width > 0 && proposedSize.Width < int.MaxValue / 2
                ? proposedSize.Width
                : Math.Max(Width, 1);
            var textWidth = Math.Max(width - Padding.Horizontal, 1);
            var measured = TextRenderer.MeasureText(
                Text ?? string.Empty,
                Font,
                new Size(textWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.Left);
            var height = measured.Height + Padding.Vertical;
            if (MaximumSize.Height > 0)
            {
                height = Math.Min(height, MaximumSize.Height);
            }

            return new Size(width, Math.Max(height, MinimumSize.Height));
        }

        // Lets the text grow with its content but never past this share of the container, so the controls
        // below stay usable; the accessible name still carries the full text.
        internal void LimitHeightToShareOf(Control container, float share)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            void Apply()
            {
                var limit = Math.Max(MinimumSize.Height, (int)(container.ClientSize.Height * share));
                if (limit > 0 && MaximumSize.Height != limit)
                {
                    MaximumSize = new Size(0, limit);
                }
            }

            container.SizeChanged += delegate { Apply(); };
            Apply();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var bounds = new Rectangle(0, 0, Width, Height);
            var needed = TextRenderer.MeasureText(
                Text ?? string.Empty,
                Font,
                new Size(Math.Max(Width, 1), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.Left).Height;
            // Text that does not fit starts at the top and ends with an ellipsis instead of losing both ends.
            var alignment = needed <= Height
                ? TextFormatFlags.VerticalCenter
                : TextFormatFlags.Top | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                bounds,
                ForeColor,
                BackColor,
                TextFormatFlags.WordBreak | TextFormatFlags.Left | alignment);

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
