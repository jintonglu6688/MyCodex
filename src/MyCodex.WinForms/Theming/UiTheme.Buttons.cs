using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal static partial class UiTheme
    {
        private static readonly ConditionalWeakTable<Button, ButtonThemeState> InitializedButtons =
            new ConditionalWeakTable<Button, ButtonThemeState>();

        public static void ApplyButton(Button button, UiGlyph glyph)
        {
            if (button == null)
            {
                return;
            }

            var state = GetOrCreateButtonState(button);
            if (!state.LayoutInitialized)
            {
                state.LayoutInitialized = true;
                button.Height = ButtonHeight;
                button.Padding = new Padding(7, 0, 8, 0);
            }

            state.Glyph = glyph;
            state.HasGlyph = true;
            state.HasImage = false;
            ApplyButtonSurface(button);
        }

        internal static void ApplyButtonSurface(Button button)
        {
            if (button == null)
            {
                return;
            }

            var state = GetOrCreateButtonState(button);
            if (state.HasGlyph)
            {
                ApplyButtonImage(button, state);
                button.ImageAlign = ContentAlignment.MiddleLeft;
                button.TextAlign = ContentAlignment.MiddleCenter;
                button.TextImageRelation = TextImageRelation.ImageBeforeText;
            }

            button.ForeColor = TextColor;
            if (_currentMode != UiThemeMode.HighContrast)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                button.BackColor = CardBackColor;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = BorderColor;
                button.FlatAppearance.MouseOverBackColor = AccentSoftBackColor;
                button.FlatAppearance.MouseDownBackColor = SelectedBackColor;
            }
            else
            {
                button.FlatStyle = FlatStyle.Standard;
                button.UseVisualStyleBackColor = true;
            }

            button.Invalidate();
        }

        public static void ApplyDialogButton(Button button, UiGlyph glyph)
        {
            ApplyButton(button, glyph);
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowOnly;
            button.MinimumSize = new Size(ButtonWidth, ButtonHeight);
            button.Margin = new Padding(8, 0, 0, 0);
        }

        private static void ApplyButtonImage(Button button, ButtonThemeState state)
        {
            if (state.HasImage && state.ImageMode == _currentMode)
            {
                return;
            }

            var previousImage = state.Image;
            var image = CreateGlyph(state.Glyph, ButtonIconSize, _currentMode);
            button.Image = image;
            state.Image = image;
            state.ImageMode = _currentMode;
            state.HasImage = true;
            if (previousImage != null)
            {
                previousImage.Dispose();
            }
        }

        private static ButtonThemeState GetOrCreateButtonState(Button button)
        {
            ButtonThemeState state;
            if (InitializedButtons.TryGetValue(button, out state))
            {
                return state;
            }

            state = new ButtonThemeState();
            InitializedButtons.Add(button, state);
            button.Paint += PaintThemedButton;
            button.MouseEnter += (s, e) => SetButtonInteraction(button, hover: true, pressed: null);
            button.MouseLeave += (s, e) => SetButtonInteraction(button, hover: false, pressed: false);
            button.MouseDown += (s, e) => SetButtonInteraction(button, hover: true, pressed: true);
            button.MouseUp += (s, e) => SetButtonInteraction(button, hover: true, pressed: false);
            button.EnabledChanged += (s, e) => button.Invalidate();
            button.GotFocus += (s, e) => button.Invalidate();
            button.LostFocus += (s, e) => button.Invalidate();
            button.Disposed += (s, e) => state.Dispose();
            return state;
        }

        private static void SetButtonInteraction(Button button, bool hover, bool? pressed)
        {
            ButtonThemeState state;
            if (!InitializedButtons.TryGetValue(button, out state))
            {
                return;
            }

            state.Hovered = hover;
            if (pressed.HasValue)
            {
                state.Pressed = pressed.Value;
            }

            button.Invalidate();
        }

        private static void PaintThemedButton(object sender, PaintEventArgs e)
        {
            var button = sender as Button;
            if (button == null || _currentMode == UiThemeMode.HighContrast)
            {
                return;
            }

            ButtonThemeState state;
            if (!InitializedButtons.TryGetValue(button, out state))
            {
                return;
            }

            var background = !button.Enabled
                ? SubtleBackColor
                : state.Pressed
                    ? SelectedBackColor
                    : state.Hovered ? AccentSoftBackColor : CardBackColor;
            var foreground = !button.Enabled
                ? MutedTextColor
                : state.Pressed ? SelectedTextColor : TextColor;
            using (var backgroundBrush = new SolidBrush(background))
            using (var borderPen = new Pen(button.Focused ? ActiveBorderColor : BorderColor))
            {
                e.Graphics.FillRectangle(backgroundBrush, button.ClientRectangle);
                e.Graphics.DrawRectangle(
                    borderPen,
                    0,
                    0,
                    Math.Max(0, button.ClientSize.Width - 1),
                    Math.Max(0, button.ClientSize.Height - 1));
            }

            var image = button.Image;
            var imageWidth = image == null ? 0 : image.Width + Scale(6);
            var text = button.Text ?? string.Empty;
            var textSize = TextRenderer.MeasureText(
                e.Graphics,
                text,
                button.Font,
                Size.Empty,
                TextFormatFlags.SingleLine);
            var totalWidth = imageWidth + textSize.Width;
            var startX = Math.Max(Scale(5), (button.ClientSize.Width - totalWidth) / 2);
            if (image != null)
            {
                e.Graphics.DrawImage(
                    image,
                    startX,
                    Math.Max(0, (button.ClientSize.Height - image.Height) / 2),
                    image.Width,
                    image.Height);
            }

            var textBounds = new Rectangle(
                startX + imageWidth,
                0,
                Math.Max(0, button.ClientSize.Width - startX - imageWidth - Scale(5)),
                button.ClientSize.Height);
            TextRenderer.DrawText(
                e.Graphics,
                text,
                button.Font,
                textBounds,
                foreground,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis);
            if (button.Focused)
            {
                ControlPaint.DrawFocusRectangle(
                    e.Graphics,
                    Rectangle.Inflate(textBounds, -Scale(2), -Scale(4)),
                    foreground,
                    background);
            }
        }

        private sealed class ButtonThemeState : IDisposable
        {
            public UiGlyph Glyph { get; set; }

            public UiThemeMode ImageMode { get; set; }

            public bool HasGlyph { get; set; }

            public bool HasImage { get; set; }

            public Bitmap Image { get; set; }

            public bool LayoutInitialized { get; set; }

            public bool Hovered { get; set; }

            public bool Pressed { get; set; }

            public void Dispose()
            {
                if (Image != null)
                {
                    Image.Dispose();
                    Image = null;
                }
            }
        }
    }
}
