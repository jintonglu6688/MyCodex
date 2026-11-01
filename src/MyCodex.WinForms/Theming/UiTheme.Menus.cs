using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal static partial class UiTheme
    {
        private static readonly ConditionalWeakTable<MenuItem, MenuItemThemeState> MenuItemStates =
            new ConditionalWeakTable<MenuItem, MenuItemThemeState>();

        private static void ApplyMainMenu(MainMenu menu)
        {
            ApplyMenuItems(menu == null ? null : menu.MenuItems);
        }

        internal static void ApplyContextMenu(ContextMenu menu)
        {
            ApplyMenuItems(menu == null ? null : menu.MenuItems);
        }

        internal static void ApplyMainShellTheme(ContextMenu trayMenu, StatusStrip statusStrip)
        {
            ApplyContextMenu(trayMenu);
            ApplyStatusStrip(statusStrip);
        }

        private static void ApplyMenuItems(Menu.MenuItemCollection items)
        {
            if (items == null)
            {
                return;
            }

            foreach (MenuItem item in items)
            {
                ApplyMenuItem(item);
            }
        }

        private static void ApplyMenuItem(MenuItem item)
        {
            if (item == null)
            {
                return;
            }

            MenuItemThemeState state;
            if (!MenuItemStates.TryGetValue(item, out state))
            {
                state = new MenuItemThemeState { OwnerDraw = item.OwnerDraw };
                MenuItemStates.Add(item, state);
                item.DrawItem += DrawMenuItem;
                item.MeasureItem += MeasureMenuItem;
                item.Popup += (s, e) => ApplyMenuItems(item.MenuItems);
            }

            item.OwnerDraw = _currentMode != UiThemeMode.HighContrast || state.OwnerDraw;
            ApplyMenuItems(item.MenuItems);
        }

        private static void MeasureMenuItem(object sender, MeasureItemEventArgs e)
        {
            var item = sender as MenuItem;
            if (item == null || _currentMode == UiThemeMode.HighContrast)
            {
                return;
            }

            if (string.Equals(item.Text, "-", StringComparison.Ordinal))
            {
                e.ItemHeight = Scale(9);
                e.ItemWidth = Scale(180);
                return;
            }

            var font = SystemInformation.MenuFont;
            var text = StripMnemonic(item.Text);
            var textSize = TextRenderer.MeasureText(
                text,
                font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (item.Parent is MainMenu)
            {
                e.ItemHeight = Math.Max(Scale(24), textSize.Height + Scale(8));
                e.ItemWidth = textSize.Width + Scale(24);
                return;
            }

            var shortcut = GetMenuShortcutText(item);
            var shortcutWidth = string.IsNullOrEmpty(shortcut)
                ? 0
                : TextRenderer.MeasureText(shortcut, font).Width;
            e.ItemHeight = Math.Max(Scale(26), textSize.Height + Scale(8));
            e.ItemWidth = Scale(28) + textSize.Width + Scale(18) +
                (shortcutWidth > 0 ? shortcutWidth + Scale(18) : 0) +
                (item.IsParent ? Scale(20) : 0);
        }

        private static void DrawMenuItem(object sender, DrawItemEventArgs e)
        {
            var item = sender as MenuItem;
            if (item == null)
            {
                return;
            }

            if (_currentMode == UiThemeMode.HighContrast)
            {
                return;
            }

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var topLevel = item.Parent is MainMenu;
            var background = selected
                ? SelectedBackColor
                : topLevel ? WindowBackColor : CardBackColor;
            using (var backgroundBrush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
            }

            if (string.Equals(item.Text, "-", StringComparison.Ordinal))
            {
                using (var separatorPen = new Pen(BorderColor))
                {
                    var y = e.Bounds.Top + e.Bounds.Height / 2;
                    e.Graphics.DrawLine(separatorPen, e.Bounds.Left + Scale(8), y, e.Bounds.Right - Scale(8), y);
                }
                return;
            }

            var foreground = !item.Enabled ? MutedTextColor : selected ? SelectedTextColor : TextColor;
            if (topLevel)
            {
                var topLevelTextBounds = Rectangle.Inflate(e.Bounds, -Scale(8), 0);
                TextRenderer.DrawText(
                    e.Graphics,
                    item.Text,
                    SystemInformation.MenuFont,
                    topLevelTextBounds,
                    foreground,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis);
                return;
            }

            var gutterWidth = Scale(28);
            if (item.Checked)
            {
                using (var checkPen = new Pen(foreground, Math.Max(1.5F, (float)Scale(2))))
                {
                    var left = e.Bounds.Left + Scale(8);
                    var top = e.Bounds.Top + e.Bounds.Height / 2 - Scale(2);
                    e.Graphics.DrawLines(checkPen, new[]
                    {
                        new Point(left, top),
                        new Point(left + Scale(3), top + Scale(4)),
                        new Point(left + Scale(9), top - Scale(4))
                    });
                }
            }

            var shortcut = GetMenuShortcutText(item);
            var arrowWidth = item.IsParent ? Scale(20) : 0;
            var shortcutWidth = string.IsNullOrEmpty(shortcut)
                ? 0
                : TextRenderer.MeasureText(shortcut, SystemInformation.MenuFont).Width + Scale(16);
            var textBounds = new Rectangle(
                e.Bounds.Left + gutterWidth,
                e.Bounds.Top,
                Math.Max(0, e.Bounds.Width - gutterWidth - arrowWidth - shortcutWidth - Scale(8)),
                e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                item.Text,
                SystemInformation.MenuFont,
                textBounds,
                foreground,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis);

            if (!string.IsNullOrEmpty(shortcut))
            {
                var shortcutBounds = new Rectangle(
                    e.Bounds.Right - arrowWidth - shortcutWidth,
                    e.Bounds.Top,
                    shortcutWidth - Scale(8),
                    e.Bounds.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    shortcut,
                    SystemInformation.MenuFont,
                    shortcutBounds,
                    selected ? SelectedTextColor : MutedTextColor,
                    TextFormatFlags.Right |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPrefix);
            }

            if (item.IsParent)
            {
                var centerX = e.Bounds.Right - Scale(10);
                var centerY = e.Bounds.Top + e.Bounds.Height / 2;
                using (var arrowPen = new Pen(foreground, Math.Max(1.4F, (float)Scale(2))))
                {
                    e.Graphics.DrawLines(arrowPen, new[]
                    {
                        new Point(centerX - Scale(2), centerY - Scale(4)),
                        new Point(centerX + Scale(2), centerY),
                        new Point(centerX - Scale(2), centerY + Scale(4))
                    });
                }
            }

            if (selected)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -Scale(2), -Scale(2)), foreground, background);
            }
        }

        private static string GetMenuShortcutText(MenuItem item)
        {
            return item.Shortcut == Shortcut.None
                ? string.Empty
                : TypeDescriptor.GetConverter(typeof(Keys)).ConvertToString((Keys)item.Shortcut);
        }

        private static string StripMnemonic(string text)
        {
            return (text ?? string.Empty).Replace("&&", "\0").Replace("&", string.Empty).Replace("\0", "&");
        }

        private sealed class MenuItemThemeState
        {
            public bool OwnerDraw { get; set; }
        }
    }
}
