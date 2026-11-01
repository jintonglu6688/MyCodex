using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal static partial class UiTheme
    {
        internal static void ApplyToolStripItems(ToolStripItemCollection items)
        {
            if (items == null)
            {
                return;
            }

            foreach (ToolStripItem item in items)
            {
                if (item == null)
                {
                    continue;
                }

                item.ForeColor = item.Enabled ? TextColor : MutedTextColor;
                var dropDownItem = item as ToolStripDropDownItem;
                if (dropDownItem == null)
                {
                    continue;
                }

                ApplyToolStripItems(dropDownItem.DropDownItems);
                ApplyToolStrip(dropDownItem.DropDown);
            }
        }

        private sealed class ThemedToolStripRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var brush = new SolidBrush(e.ToolStrip is StatusStrip ? PanelBackColor : WindowBackColor))
                {
                    e.Graphics.FillRectangle(brush, e.AffectedBounds);
                }
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                if (e.ToolStrip is StatusStrip || e.ToolStrip is ToolStripDropDown)
                {
                    return;
                }

                using (var pen = new Pen(BorderColor))
                {
                    e.Graphics.DrawRectangle(
                        pen,
                        0,
                        0,
                        Math.Max(0, e.ToolStrip.Width - 1),
                        Math.Max(0, e.ToolStrip.Height - 1));
                }
            }

            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            {
                DrawToolStripButtonBackground(e);
            }

            protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
            {
                DrawToolStripButtonBackground(e);
            }

            protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
            {
                DrawToolStripButtonBackground(e);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = !e.Item.Enabled
                    ? MutedTextColor
                    : e.Item.Pressed ? SelectedTextColor : TextColor;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = e.Item != null && !e.Item.Enabled ? MutedTextColor : TextColor;
                base.OnRenderArrow(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                var bounds = e.Item.ContentRectangle;
                using (var pen = new Pen(BorderColor))
                {
                    if (e.Vertical)
                    {
                        var x = bounds.Left + bounds.Width / 2;
                        e.Graphics.DrawLine(pen, x, bounds.Top + Scale(4), x, bounds.Bottom - Scale(4));
                    }
                    else
                    {
                        var y = bounds.Top + bounds.Height / 2;
                        e.Graphics.DrawLine(pen, bounds.Left + Scale(4), y, bounds.Right - Scale(4), y);
                    }
                }
            }

            protected override void OnRenderGrip(ToolStripGripRenderEventArgs e)
            {
                using (var brush = new SolidBrush(BorderColor))
                {
                    if (e.GripStyle == ToolStripGripStyle.Visible)
                    {
                        e.Graphics.FillRectangle(brush, e.GripBounds);
                    }
                }
            }

            protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
            {
                DrawToolStripButtonBackground(e);
            }

            private static void DrawToolStripButtonBackground(ToolStripItemRenderEventArgs e)
            {
                if (e.Item == null)
                {
                    return;
                }

                var background = !e.Item.Enabled
                    ? SubtleBackColor
                    : e.Item.Pressed
                        ? SelectedBackColor
                        : e.Item.Selected ? AccentSoftBackColor : Color.Transparent;
                if (background == Color.Transparent)
                {
                    return;
                }

                using (var brush = new SolidBrush(background))
                using (var pen = new Pen(e.Item.Pressed || e.Item.Selected ? ActiveBorderColor : BorderColor))
                {
                    var bounds = new Rectangle(0, 0, Math.Max(0, e.Item.Width - 1), Math.Max(0, e.Item.Height - 1));
                    e.Graphics.FillRectangle(brush, bounds);
                    e.Graphics.DrawRectangle(pen, bounds);
                }
            }
        }
    }
}
