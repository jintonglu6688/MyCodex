using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal sealed class ThemedProgressBar : ProgressBar
    {
        private const int WindowPaint = 0x000F;
        private const int WindowPrint = 0x0317;
        private const int WindowPrintClient = 0x0318;
        private readonly Timer _marqueeTimer;

        public ThemedProgressBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
            _marqueeTimer = new Timer { Interval = 33 };
            _marqueeTimer.Tick += (s, e) =>
            {
                if (Visible)
                {
                    Invalidate();
                }
            };
        }

        internal bool IsAnimatingForTests
        {
            get { return _marqueeTimer.Enabled; }
        }

        // Changing Style recreates the handle, so these hooks also catch a switch to or from Marquee.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateMarqueeTimer();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            base.OnHandleDestroyed(e);
            UpdateMarqueeTimer();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            UpdateMarqueeTimer();
        }

        private void UpdateMarqueeTimer()
        {
            _marqueeTimer.Enabled = !IsDisposed && IsHandleCreated && Visible && Style == ProgressBarStyle.Marquee;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WindowPaint && IsHandleCreated && Visible)
            {
                using (var graphics = Graphics.FromHwnd(Handle))
                {
                    PaintBar(graphics);
                }
            }
            else if ((m.Msg == WindowPrint || m.Msg == WindowPrintClient) && m.WParam != IntPtr.Zero)
            {
                using (var graphics = Graphics.FromHdc(m.WParam))
                {
                    PaintBar(graphics);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _marqueeTimer.Stop();
                _marqueeTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        private void PaintBar(Graphics graphics)
        {
            if (UiTheme.CurrentMode == UiThemeMode.HighContrast || Width <= 0 || Height <= 0)
            {
                return;
            }

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var trackBrush = new SolidBrush(UiTheme.SubtleBackColor))
            using (var fillBrush = new SolidBrush(Enabled ? UiTheme.AccentColor : UiTheme.MutedTextColor))
            using (var borderPen = new Pen(UiTheme.BorderColor))
            {
                graphics.FillRectangle(trackBrush, bounds);
                var fillBounds = ResolveFillBounds(bounds);
                if (fillBounds.Width > 0)
                {
                    graphics.FillRectangle(fillBrush, fillBounds);
                }

                graphics.DrawRectangle(borderPen, bounds);
            }
        }

        private Rectangle ResolveFillBounds(Rectangle bounds)
        {
            if (Style == ProgressBarStyle.Marquee)
            {
                var segmentWidth = Math.Max(40, bounds.Width / 3);
                var phase = (Environment.TickCount & 0x7FFFFFFF) / 12;
                var x = phase % Math.Max(1, bounds.Width + segmentWidth) - segmentWidth;
                return Rectangle.Intersect(
                    bounds,
                    new Rectangle(x, bounds.Top + 2, segmentWidth, Math.Max(0, bounds.Height - 3)));
            }

            var range = Math.Max(1, Maximum - Minimum);
            var progress = Math.Max(0, Math.Min(range, Value - Minimum));
            var width = (int)Math.Round(bounds.Width * (progress / (double)range));
            return width <= 0
                ? Rectangle.Empty
                : new Rectangle(bounds.Left, bounds.Top, width, bounds.Height);
        }
    }
}
