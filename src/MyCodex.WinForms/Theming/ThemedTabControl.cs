using System.Drawing;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal sealed class ThemedTabControl : TabControl
    {
        public ThemedTabControl()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
        }

        internal void RefreshTabLayout()
        {
            if (TabCount == 0 || !IsHandleCreated)
            {
                return;
            }

            var width = 0;
            var height = 0;
            using (var graphics = CreateGraphics())
            {
                for (var index = 0; index < TabCount; index++)
                {
                    var textSize = TextRenderer.MeasureText(
                        graphics,
                        GetTabText(index),
                        Font,
                        Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    width = System.Math.Max(width, textSize.Width + UiTheme.Scale(30));
                    height = System.Math.Max(height, textSize.Height + UiTheme.Scale(12));
                }
            }

            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(System.Math.Max(UiTheme.Scale(84), width), System.Math.Max(UiTheme.Scale(28), height));
        }

        protected override void OnFontChanged(System.EventArgs e)
        {
            base.OnFontChanged(e);
            if (IsHandleCreated)
            {
                RefreshTabLayout();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(UiTheme.PanelBackColor);
            DrawTabPages(e.Graphics);
            DrawFrame(e.Graphics);
            base.OnPaint(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(UiTheme.PanelBackColor);
        }

        protected override void OnSelectedIndexChanged(System.EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(System.EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(System.EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        private void DrawTabPages(Graphics graphics)
        {
            if (TabCount == 0)
            {
                return;
            }

            var normalBackColor = UiTheme.PanelBackColor;
            var normalTextColor = UiTheme.MutedTextColor;
            var selectedBackColor = UiTheme.CardBackColor;
            var selectedTextColor = UiTheme.TextColor;

            using (var normalBrush = new SolidBrush(normalBackColor))
            using (var selectedBrush = new SolidBrush(selectedBackColor))
            using (var normalTextBrush = new SolidBrush(normalTextColor))
            using (var selectedTextBrush = new SolidBrush(selectedTextColor))
            using (var borderPen = new Pen(UiTheme.BorderColor))
            {
                for (var index = 0; index < TabCount; index++)
                {
                    var bounds = GetTabRect(index);
                    var selected = index == SelectedIndex;
                    graphics.FillRectangle(selected ? selectedBrush : normalBrush, bounds);
                    graphics.DrawRectangle(borderPen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
                    var textBounds = Rectangle.Inflate(bounds, -UiTheme.Scale(10), -UiTheme.Scale(2));
                    TextRenderer.DrawText(
                        graphics,
                        GetTabText(index),
                        Font,
                        textBounds,
                        selected ? selectedTextColor : normalTextColor,
                        TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine |
                        TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPrefix);
                    if (selected && Focused)
                    {
                        ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(textBounds, -2, -2), selectedTextColor, selectedBackColor);
                    }
                }
            }
        }

        private void DrawFrame(Graphics graphics)
        {
            var displayRectangle = DisplayRectangle;
            if (displayRectangle.Width <= 0 || displayRectangle.Height <= 0)
            {
                return;
            }

            using (var pageBrush = new SolidBrush(UiTheme.WindowBackColor))
            using (var borderPen = new Pen(UiTheme.BorderColor))
            {
                graphics.FillRectangle(pageBrush, displayRectangle);
                graphics.DrawRectangle(
                    borderPen,
                    displayRectangle.Left - 1,
                    displayRectangle.Top - 1,
                    displayRectangle.Width + 1,
                    displayRectangle.Height + 1);
            }
        }

        private string GetTabText(int index)
        {
            var tabPage = TabPages[index];
            return string.IsNullOrEmpty(tabPage.Text) ? tabPage.Name : tabPage.Text;
        }
    }
}
