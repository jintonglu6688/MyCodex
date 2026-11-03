using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Theming
{
    internal static partial class UiTheme
    {
        private static string _uiFontFamily;
        private static readonly ConditionalWeakTable<Form, object> InitializedThemeForms = new ConditionalWeakTable<Form, object>();
        private static readonly ConditionalWeakTable<TextBox, object> InitializedTextBoxes = new ConditionalWeakTable<TextBox, object>();
        private static readonly ConditionalWeakTable<Control, object> ControlAddedThemeSubscriptions =
            new ConditionalWeakTable<Control, object>();
        private static UiThemeMode _currentMode = ResolveMode(
            UiThemePreference.Auto,
            SystemInformation.HighContrast,
            SystemThemeDetector.UsesDarkTheme());

        public enum StatusType
        {
            Ready,
            Processing,
            Error
        }

        public const float BodyFontSize = 9F;
        public const float ReadingFontSize = 10F;
        public const float SubtitleFontSize = 10F;
        public const float TitleFontSize = 11F;

        public const int OuterPadding = 14;
        public const int SectionGap = 10;
        public const int InnerPadding = 16;
        public const int ControlSpacing = 8;
        public const int ButtonHeight = 32;
        public const int ButtonWidth = 96;
        public const int ButtonIconSize = 16;
        public const int SectionTitleHeight = 32;
        public const int SubSectionTitleHeight = 28;
        public const int HeaderPanelHeight = 184;
        public const int SummaryPanelHeight = 120;
        public const int ComposePanelHeight = 118;
        public const int AttachmentPanelHeight = 96;

        public static bool IsHighContrast
        {
            get { return SystemInformation.HighContrast; }
        }

        internal static UiThemeMode CurrentMode
        {
            get { return _currentMode; }
        }

        public static Color WindowBackColor
        {
            get { return GetPalette(_currentMode).WindowBackColor; }
        }

        public static Color PanelBackColor
        {
            get { return GetPalette(_currentMode).PanelBackColor; }
        }

        public static Color CardBackColor
        {
            get { return GetPalette(_currentMode).CardBackColor; }
        }

        public static Color SubtleBackColor
        {
            get { return GetPalette(_currentMode).SubtleBackColor; }
        }

        public static Color BorderColor
        {
            get { return GetPalette(_currentMode).BorderColor; }
        }

        public static Color ActiveBorderColor
        {
            get { return GetPalette(_currentMode).ActiveBorderColor; }
        }

        public static Color SelectedBackColor
        {
            get { return GetPalette(_currentMode).SelectedBackColor; }
        }

        public static Color SelectedTextColor
        {
            get { return GetPalette(_currentMode).SelectedTextColor; }
        }

        public static Color TextColor
        {
            get { return GetPalette(_currentMode).TextColor; }
        }

        public static Color MutedTextColor
        {
            get { return GetPalette(_currentMode).MutedTextColor; }
        }

        public static Color DisabledTextColor
        {
            get { return GetPalette(_currentMode).DisabledTextColor; }
        }

        public static Color AccentColor
        {
            get { return GetPalette(_currentMode).AccentColor; }
        }

        public static Color AccentSoftBackColor
        {
            get { return GetPalette(_currentMode).AccentSoftBackColor; }
        }

        internal static string ResolveUiFontFamily(string cultureName)
        {
            return string.Equals(cultureName, "zh-CN", StringComparison.OrdinalIgnoreCase)
                ? "Microsoft YaHei UI"
                : "Segoe UI";
        }

        internal static void InitializeUiFontFamily(string cultureName)
        {
            // Startup calls this after applying the target language; later language changes wait for restart.
            if (_uiFontFamily == null)
            {
                _uiFontFamily = ResolveUiFontFamily(cultureName);
            }
        }

        public static Font CreateUiFont(float size, FontStyle style)
        {
            // Migration dialogs can appear before the target language has been applied.
            return new Font(_uiFontFamily ?? ResolveUiFontFamily(CultureInfo.CurrentUICulture.Name), size * UserFontScale, style, GraphicsUnit.Point);
        }

        public static Font CreateCodeFont(float size, FontStyle style)
        {
            return new Font("Consolas", size * UserFontScale, style, GraphicsUnit.Point);
        }

        public static void ApplyForm(Form form)
        {
            if (form == null)
            {
                return;
            }

            MyCodex.WinForms.Forms.TargetWindowGroup.Prepare(form);
            InitializeFormScaling(form);
            form.BackColor = WindowBackColor;
            form.ForeColor = TextColor;
            WindowDarkMode.TryApply(
                form,
                _currentMode == UiThemeMode.Dark,
                _currentMode != UiThemeMode.HighContrast);
            if (!InitializedThemeForms.TryGetValue(form, out _))
            {
                InitializedThemeForms.Add(form, new object());
                form.Load += (s, e) =>
                {
                    ApplyToControlTree(form);
                    form.Invalidate(true);
                };
                form.Shown += (s, e) =>
                {
                    ApplyToControlTree(form);
                    form.Invalidate(true);
                };
            }
        }

        public static void ApplyListBox(ListBox listBox)
        {
            if (listBox == null)
            {
                return;
            }

            listBox.BorderStyle = BorderStyle.FixedSingle;
            NativeWindowTheme.Apply(listBox, _currentMode == UiThemeMode.Dark);
            listBox.BackColor = CardBackColor;
            listBox.ForeColor = TextColor;
            ApplyListBoxTheme(listBox);
        }

        public static void ApplyListView(ListView listView)
        {
            if (listView == null)
            {
                return;
            }

            listView.BorderStyle = BorderStyle.FixedSingle;
            NativeWindowTheme.Apply(listView, _currentMode == UiThemeMode.Dark);
            listView.BackColor = CardBackColor;
            listView.ForeColor = TextColor;
            ApplyListViewTheme(listView);
        }

        public static void ApplyComboBox(ComboBox comboBox)
        {
            if (comboBox == null)
            {
                return;
            }

            ApplyComboBoxTheme(comboBox);
        }

        public static void ApplyCheckBox(CheckBox checkBox)
        {
            if (checkBox == null)
            {
                return;
            }

            ApplyToggleTheme(checkBox, false);
        }

        public static void ApplyRadioButton(RadioButton radioButton)
        {
            if (radioButton == null)
            {
                return;
            }

            ApplyToggleTheme(radioButton, true);
        }

        public static void ApplyLabel(Label label, bool isMuted)
        {
            if (label == null)
            {
                return;
            }

            label.ForeColor = isMuted ? MutedTextColor : TextColor;
        }

        public static void ApplyTextBox(TextBox textBox, bool isCode, bool isReadOnly)
        {
            if (textBox == null)
            {
                return;
            }

            NativeWindowTheme.Apply(textBox, _currentMode == UiThemeMode.Dark);
            textBox.BackColor = isReadOnly ? PanelBackColor : GetEditableTextBackColor();
            textBox.ForeColor = TextColor;
            if (InitializedTextBoxes.TryGetValue(textBox, out _))
            {
                return;
            }

            InitializedTextBoxes.Add(textBox, new object());
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.HandleCreated += (s, e) =>
            {
                NativeWindowTheme.Apply(textBox, _currentMode == UiThemeMode.Dark);
                textBox.BackColor = textBox.ReadOnly ? PanelBackColor : GetEditableTextBackColor();
                textBox.ForeColor = TextColor;
            };
            textBox.Font = isCode
                ? CreateCodeFont(ReadingFontSize, FontStyle.Regular)
                : CreateUiFont(ReadingFontSize, FontStyle.Regular);

            bool isNormalizingText = false;
            TextBoxLineEndingNormalizer.NormalizeTextBox(textBox);
            textBox.TextChanged += (s, e) =>
            {
                if (isNormalizingText)
                {
                    return;
                }

                try
                {
                    isNormalizingText = true;
                    TextBoxLineEndingNormalizer.NormalizeTextBox(textBox);
                }
                finally
                {
                    isNormalizingText = false;
                }
            };
        }

        internal static void ApplyToOpenForms()
        {
            foreach (Form form in System.Windows.Forms.Application.OpenForms)
            {
                ApplyToControlTree(form);
                form.Invalidate(true);
                form.Update();
            }
        }

        internal static void ApplyToOpenForms(Form fallbackForm)
        {
            var fallbackApplied = false;
            foreach (Form form in System.Windows.Forms.Application.OpenForms)
            {
                ApplyToControlTree(form);
                form.Invalidate(true);
                form.Update();
                fallbackApplied = fallbackApplied || ReferenceEquals(form, fallbackForm);
            }

            if (!fallbackApplied && fallbackForm != null)
            {
                ApplyToControlTree(fallbackForm);
                fallbackForm.Invalidate(true);
                fallbackForm.Update();
            }
        }

        public static void ApplyToControlTree(Control root)
        {
            if (root == null)
            {
                return;
            }

            var form = root as Form;
            var suspendRedraw = form != null && form.IsHandleCreated && !form.IsDisposed;
            if (suspendRedraw)
            {
                form.SuspendLayout();
                NativeWindowTheme.SuspendRedraw(form);
            }

            try
            {
                ApplyToControlTreeCore(root);
            }
            finally
            {
                if (suspendRedraw)
                {
                    form.ResumeLayout(false);
                    form.PerformLayout();
                    NativeWindowTheme.ResumeRedraw(form);
                    form.Invalidate(true);
                    form.Update();
                }
            }
        }

        private static void ApplyToControlTreeCore(Control root)
        {
            SubscribeToControlAdded(root);
            ApplyControl(root);
            var form = root as Form;
            if (form != null)
            {
                ApplyMainMenu(form.Menu);
            }

            ApplyContextMenu(root.ContextMenu);
            foreach (Control child in root.Controls)
            {
                ApplyToControlTreeCore(child);
            }
        }

        private static void SubscribeToControlAdded(Control parent)
        {
            if (ControlAddedThemeSubscriptions.TryGetValue(parent, out _))
            {
                return;
            }

            ControlAddedThemeSubscriptions.Add(parent, new object());
            parent.ControlAdded += delegate(object sender, ControlEventArgs args)
            {
                var child = args == null ? null : args.Control;
                if (child == null || child.IsDisposed || !parent.IsHandleCreated || parent.IsDisposed)
                {
                    return;
                }

                try
                {
                    parent.BeginInvoke((MethodInvoker)delegate
                    {
                        if (!parent.IsDisposed && !child.IsDisposed)
                        {
                            ApplyToControlTree(child);
                            child.Invalidate(true);
                        }
                    });
                }
                catch (InvalidOperationException)
                {
                }
            };
        }

        public static void DrawCardShadow(Graphics graphics, Rectangle bounds)
        {
            if (IsHighContrast)
            {
                return;
            }

            var palette = GetPalette(_currentMode);
            var shadowSize = Scale(3);
            var shadowOffset = Scale(2);

            // Draw right shadow
            using (var brush = new LinearGradientBrush(
                new Rectangle(bounds.Right, bounds.Top + shadowOffset, shadowSize, bounds.Height),
                palette.ShadowColor,
                palette.ShadowFadeColor,
                LinearGradientMode.Horizontal))
            {
                graphics.FillRectangle(brush, bounds.Right, bounds.Top + shadowOffset, shadowSize, bounds.Height);
            }

            // Draw bottom shadow
            using (var brush = new LinearGradientBrush(
                new Rectangle(bounds.Left + shadowOffset, bounds.Bottom, bounds.Width, shadowSize),
                palette.ShadowColor,
                palette.ShadowFadeColor,
                LinearGradientMode.Vertical))
            {
                graphics.FillRectangle(brush, bounds.Left + shadowOffset, bounds.Bottom, bounds.Width, shadowSize);
            }

            // Draw corner shadow
            using (var brush = new LinearGradientBrush(
                new Rectangle(bounds.Right, bounds.Bottom, shadowSize, shadowSize),
                palette.ShadowCornerColor,
                palette.ShadowFadeColor,
                45f))
            {
                graphics.FillRectangle(brush, bounds.Right, bounds.Bottom, shadowSize, shadowSize);
            }
        }

        public static void DrawSplitterGrip(Graphics graphics, Rectangle bounds, Orientation orientation)
        {
            if (IsHighContrast)
            {
                return;
            }

            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var gripColor = BorderColor;
            var gripLength = Scale(32);
            var gripWidth = Scale(3);
            var gripSpacing = Scale(2);

            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;

            if (orientation == Orientation.Vertical)
            {
                // Vertical splitter - draw horizontal grip lines
                int startY = centerY - gripLength / 2;
                for (int i = 0; i < 3; i++)
                {
                    int y = startY + i * (gripWidth + gripSpacing);
                    using (var pen = new Pen(gripColor, gripWidth))
                    {
                        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        graphics.DrawLine(pen, centerX - Scale(8), y, centerX + Scale(8), y);
                    }
                }
            }
            else
            {
                // Horizontal splitter - draw vertical grip lines
                int startX = centerX - gripLength / 2;
                for (int i = 0; i < 3; i++)
                {
                    int x = startX + i * (gripWidth + gripSpacing);
                    using (var pen = new Pen(gripColor, gripWidth))
                    {
                        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        graphics.DrawLine(pen, x, centerY - Scale(8), x, centerY + Scale(8));
                    }
                }
            }
        }

        public static Bitmap CreateChatCategoryGlyph(ChatHistoryItem item, int size)
        {
            var glyph = GetChatGlyph(item);
            return CreateGlyph(glyph, size);
        }

        public static Color GetChatCategoryColor(ChatHistoryItem item)
        {
            if (IsHighContrast)
            {
                return SystemColors.Highlight;
            }

            var palette = GetPalette(_currentMode);

            if (item == null)
            {
                return palette.GlyphNeutralColor;
            }

            switch (item.Category)
            {
                case ChatHistoryCategory.Error:
                    return palette.GlyphRedColor;
                case ChatHistoryCategory.Approval:
                    return palette.GlyphAmberColor;
                case ChatHistoryCategory.Command:
                    return palette.GlyphIndigoColor;
                case ChatHistoryCategory.FileChange:
                    return palette.GlyphGreenColor;
                case ChatHistoryCategory.Account:
                    return palette.GlyphCyanColor;
                case ChatHistoryCategory.ProtocolWarning:
                    return palette.GlyphRedColor;
                case ChatHistoryCategory.ProtocolNotice:
                    return palette.GlyphNeutralColor;
                case ChatHistoryCategory.Processing:
                    return palette.ChatProcessingColor;
                case ChatHistoryCategory.System:
                    return palette.GlyphNeutralColor;
                default:
                    return IsCodexSender(item.Sender)
                        ? palette.GlyphBlueColor
                        : palette.GlyphTealColor;
            }
        }

        public static Color GetChatCategoryBackColor(ChatHistoryItem item)
        {
            if (IsHighContrast)
            {
                return SystemColors.Window;
            }

            var palette = GetPalette(_currentMode);

            if (item == null)
            {
                return CardBackColor;
            }

            switch (item.Category)
            {
                case ChatHistoryCategory.Error:
                    return palette.ChatErrorBackColor;
                case ChatHistoryCategory.Approval:
                    return palette.ChatApprovalBackColor;
                case ChatHistoryCategory.Command:
                    return palette.ChatCommandBackColor;
                case ChatHistoryCategory.FileChange:
                    return palette.ChatFileChangeBackColor;
                case ChatHistoryCategory.Account:
                    return palette.ChatAccountBackColor;
                case ChatHistoryCategory.ProtocolWarning:
                    return palette.ChatErrorBackColor;
                case ChatHistoryCategory.ProtocolNotice:
                    return palette.ChatSystemBackColor;
                case ChatHistoryCategory.Processing:
                    return palette.ChatProcessingBackColor;
                case ChatHistoryCategory.System:
                    return palette.ChatSystemBackColor;
                default:
                    return IsCodexSender(item.Sender)
                        ? palette.ChatCodexBackColor
                        : palette.ChatUserBackColor;
            }
        }

        public static Bitmap CreateGlyph(UiGlyph glyph, int size)
        {
            return CreateGlyph(glyph, size, _currentMode);
        }

        private static Bitmap CreateGlyph(UiGlyph glyph, int size, UiThemeMode mode)
        {
            var bitmap = new Bitmap(Scale(size), Scale(size));
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.ScaleTransform(ScaleFactor, ScaleFactor);
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                var color = GetGlyphColor(glyph, mode);
                using (var pen = new Pen(color, Math.Max(1.4F, size / 10F)))
                using (var thinPen = new Pen(color, 1.2F))
                using (var brush = new SolidBrush(color))
                using (var lightBrush = new SolidBrush(GetGlyphContrastColor(mode)))
                {
                    DrawGlyph(graphics, glyph, size, pen, thinPen, brush, lightBrush);
                }
            }

            return bitmap;
        }

        public static Bitmap CreateStatusIndicator(Color color, int size)
        {
            var bitmap = new Bitmap(Scale(size), Scale(size));
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.ScaleTransform(ScaleFactor, ScaleFactor);
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using (var brush = new SolidBrush(color))
                {
                    var margin = size / 4;
                    graphics.FillEllipse(brush, margin, margin, size - margin * 2, size - margin * 2);
                }
            }

            return bitmap;
        }

        public static ToolStripStatusLabel CreateStatusIndicator()
        {
            return new ToolStripStatusLabel
            {
                Image = CreateStatusIndicator(StatusType.Ready),
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                AutoSize = false,
                ImageScaling = ToolStripItemImageScaling.None,
                Width = Scale(20)
            };
        }

        public static Bitmap CreateStatusIndicator(StatusType statusType)
        {
            const int size = 16;
            var palette = GetPalette(_currentMode);
            Color color;

            switch (statusType)
            {
                case StatusType.Error:
                    color = IsHighContrast ? SystemColors.HighlightText : palette.StatusErrorColor;
                    break;
                case StatusType.Processing:
                    color = IsHighContrast ? SystemColors.HighlightText : palette.StatusProcessingColor;
                    break;
                case StatusType.Ready:
                default:
                    color = IsHighContrast ? SystemColors.HighlightText : palette.StatusReadyColor;
                    break;
            }

            return CreateStatusIndicator(color, size);
        }

        private static void DrawGlyph(Graphics graphics, UiGlyph glyph, int size, Pen pen, Pen thinPen, Brush brush, Brush lightBrush)
        {
            var inset = Math.Max(2, size / 8);
            var rect = new Rectangle(inset, inset, size - inset * 2 - 1, size - inset * 2 - 1);

            switch (glyph)
            {
                case UiGlyph.Command:
                case UiGlyph.CommandOutput:
                    graphics.DrawRectangle(pen, rect);
                    graphics.DrawLine(pen, rect.Left + 3, rect.Top + 4, rect.Left + 6, rect.Top + rect.Height / 2);
                    graphics.DrawLine(pen, rect.Left + 6, rect.Top + rect.Height / 2, rect.Left + 3, rect.Bottom - 4);
                    graphics.DrawLine(thinPen, rect.Left + 8, rect.Bottom - 4, rect.Right - 3, rect.Bottom - 4);
                    break;
                case UiGlyph.Attach:
                    graphics.DrawArc(pen, rect.Left + 2, rect.Top, rect.Width - 4, rect.Height + 3, 210, 250);
                    graphics.DrawArc(thinPen, rect.Left + 5, rect.Top + 3, rect.Width - 9, rect.Height - 2, 210, 250);
                    break;
                case UiGlyph.Send:
                    graphics.FillPolygon(brush, new[]
                    {
                        new Point(rect.Left, rect.Top + 2),
                        new Point(rect.Right, rect.Top + rect.Height / 2),
                        new Point(rect.Left, rect.Bottom - 2),
                        new Point(rect.Left + 3, rect.Top + rect.Height / 2)
                    });
                    break;
                case UiGlyph.Stop:
                    graphics.FillRectangle(brush, rect.Left + 2, rect.Top + 2, rect.Width - 4, rect.Height - 4);
                    break;
                case UiGlyph.Ok:
                    graphics.DrawLine(pen, rect.Left + 2, rect.Top + rect.Height / 2, rect.Left + 6, rect.Bottom - 3);
                    graphics.DrawLine(pen, rect.Left + 6, rect.Bottom - 3, rect.Right - 2, rect.Top + 3);
                    break;
                case UiGlyph.Cancel:
                    graphics.DrawLine(pen, rect.Left + 3, rect.Top + 3, rect.Right - 3, rect.Bottom - 3);
                    graphics.DrawLine(pen, rect.Right - 3, rect.Top + 3, rect.Left + 3, rect.Bottom - 3);
                    break;
                case UiGlyph.Workspace:
                    graphics.FillPolygon(brush, new[]
                    {
                        new Point(rect.Left, rect.Top + 4),
                        new Point(rect.Left + 5, rect.Top + 4),
                        new Point(rect.Left + 7, rect.Top + 7),
                        new Point(rect.Right, rect.Top + 7),
                        new Point(rect.Right, rect.Bottom),
                        new Point(rect.Left, rect.Bottom)
                    });
                    break;
                case UiGlyph.NewSession:
                    graphics.DrawRectangle(pen, rect.Left, rect.Top + 2, rect.Width, rect.Height - 4);
                    graphics.DrawLine(thinPen, rect.Left + 5, rect.Bottom - 2, rect.Left + 8, rect.Bottom + 1);
                    DrawPlus(graphics, pen, rect.Right - 5, rect.Top + 6, 3);
                    break;
                case UiGlyph.Model:
                    graphics.DrawEllipse(pen, rect);
                    graphics.DrawEllipse(thinPen, rect.Left + 4, rect.Top + 4, rect.Width - 8, rect.Height - 8);
                    graphics.DrawLine(thinPen, rect.Left + 2, rect.Top + rect.Height / 2, rect.Right - 2, rect.Top + rect.Height / 2);
                    break;
                case UiGlyph.Search:
                    graphics.DrawEllipse(pen, rect.Left, rect.Top, rect.Width - 4, rect.Height - 4);
                    graphics.DrawLine(pen, rect.Right - 4, rect.Bottom - 4, rect.Right, rect.Bottom);
                    break;
                case UiGlyph.Refresh:
                    graphics.DrawArc(pen, rect, 35, 285);
                    graphics.FillPolygon(brush, new[]
                    {
                        new Point(rect.Right - 1, rect.Top + 3),
                        new Point(rect.Right - 1, rect.Top + 8),
                        new Point(rect.Right - 6, rect.Top + 5)
                    });
                    break;
                case UiGlyph.Download:
                    graphics.DrawLine(pen, rect.Left + rect.Width / 2, rect.Top + 1, rect.Left + rect.Width / 2, rect.Bottom - 5);
                    graphics.DrawLine(pen, rect.Left + 4, rect.Top + rect.Height / 2 + 1, rect.Left + rect.Width / 2, rect.Bottom - 5);
                    graphics.DrawLine(pen, rect.Right - 4, rect.Top + rect.Height / 2 + 1, rect.Left + rect.Width / 2, rect.Bottom - 5);
                    graphics.DrawLine(pen, rect.Left + 2, rect.Bottom - 1, rect.Right - 2, rect.Bottom - 1);
                    break;
                case UiGlyph.Delete:
                    graphics.DrawLine(pen, rect.Left + 3, rect.Top + 4, rect.Right - 3, rect.Top + 4);
                    graphics.DrawLine(pen, rect.Left + 6, rect.Top + 2, rect.Right - 6, rect.Top + 2);
                    graphics.DrawRectangle(pen, rect.Left + 4, rect.Top + 5, rect.Width - 8, rect.Height - 5);
                    graphics.DrawLine(thinPen, rect.Left + 7, rect.Top + 7, rect.Left + 7, rect.Bottom - 2);
                    graphics.DrawLine(thinPen, rect.Right - 7, rect.Top + 7, rect.Right - 7, rect.Bottom - 2);
                    break;
                case UiGlyph.Error:
                    graphics.FillEllipse(brush, rect);
                    graphics.FillRectangle(lightBrush, rect.Left + rect.Width / 2 - 1, rect.Top + 3, 2, rect.Height - 8);
                    graphics.FillEllipse(lightBrush, rect.Left + rect.Width / 2 - 1, rect.Bottom - 4, 2, 2);
                    break;
                case UiGlyph.Approval:
                    graphics.FillEllipse(brush, rect);
                    using (var checkPen = new Pen(((SolidBrush)lightBrush).Color, 2F))
                    {
                        graphics.DrawLine(checkPen, rect.Left + 4, rect.Top + rect.Height / 2, rect.Left + 7, rect.Bottom - 4);
                        graphics.DrawLine(checkPen, rect.Left + 7, rect.Bottom - 4, rect.Right - 3, rect.Top + 4);
                    }
                    break;
                case UiGlyph.Microphone:
                    graphics.FillEllipse(brush, rect.Left + rect.Width / 2 - 3, rect.Top, 6, rect.Height / 2 + 2);
                    graphics.DrawArc(pen, rect.Left + 2, rect.Top + 2, rect.Width - 4, rect.Height / 2 + 3, 0, 180);
                    graphics.DrawLine(pen, rect.Left + rect.Width / 2, rect.Top + rect.Height / 2 + 5, rect.Left + rect.Width / 2, rect.Bottom - 1);
                    graphics.DrawLine(pen, rect.Left + rect.Width / 2 - 3, rect.Bottom - 1, rect.Left + rect.Width / 2 + 3, rect.Bottom - 1);
                    break;
                case UiGlyph.FileChange:
                    graphics.DrawRectangle(pen, rect.Left + 2, rect.Top, rect.Width - 4, rect.Height);
                    graphics.DrawLine(thinPen, rect.Left + 5, rect.Top + 5, rect.Right - 4, rect.Top + 5);
                    graphics.DrawLine(thinPen, rect.Left + 5, rect.Top + 8, rect.Right - 4, rect.Top + 8);
                    graphics.DrawLine(thinPen, rect.Left + 5, rect.Top + 11, rect.Right - 6, rect.Top + 11);
                    break;
                default:
                    graphics.FillEllipse(brush, rect);
                    break;
            }
        }

        private static void DrawPlus(Graphics graphics, Pen pen, int centerX, int centerY, int radius)
        {
            graphics.DrawLine(pen, centerX - radius, centerY, centerX + radius, centerY);
            graphics.DrawLine(pen, centerX, centerY - radius, centerX, centerY + radius);
        }

        private static UiGlyph GetChatGlyph(ChatHistoryItem item)
        {
            if (item == null)
            {
                return UiGlyph.System;
            }

            switch (item.Category)
            {
                case ChatHistoryCategory.Error:
                    return UiGlyph.Error;
                case ChatHistoryCategory.Approval:
                    return UiGlyph.Approval;
                case ChatHistoryCategory.Command:
                    return UiGlyph.CommandOutput;
                case ChatHistoryCategory.FileChange:
                    return UiGlyph.FileChange;
                case ChatHistoryCategory.Account:
                    return UiGlyph.Account;
                case ChatHistoryCategory.ProtocolWarning:
                    return UiGlyph.Error;
                case ChatHistoryCategory.ProtocolNotice:
                    return UiGlyph.System;
                case ChatHistoryCategory.Processing:
                    return UiGlyph.Processing;
                case ChatHistoryCategory.System:
                    return UiGlyph.System;
                default:
                    return IsCodexSender(item.Sender) ? UiGlyph.Codex : UiGlyph.User;
            }
        }

        private static Color GetGlyphColor(UiGlyph glyph, UiThemeMode mode)
        {
            if (IsHighContrast)
            {
                return SystemColors.ControlText;
            }

            var palette = GetPalette(mode);

            switch (glyph)
            {
                case UiGlyph.Send:
                case UiGlyph.Codex:
                case UiGlyph.Model:
                case UiGlyph.Search:
                case UiGlyph.Download:
                    return palette.GlyphBlueColor;
                case UiGlyph.Stop:
                case UiGlyph.Error:
                case UiGlyph.Cancel:
                case UiGlyph.Delete:
                    return palette.GlyphRedColor;
                case UiGlyph.Attach:
                case UiGlyph.Workspace:
                case UiGlyph.FileChange:
                case UiGlyph.Ok:
                    return palette.GlyphGreenColor;
                case UiGlyph.NewSession:
                case UiGlyph.User:
                case UiGlyph.Microphone:
                    return palette.GlyphTealColor;
                case UiGlyph.Processing:
                case UiGlyph.Approval:
                case UiGlyph.Refresh:
                    return palette.GlyphAmberColor;
                case UiGlyph.Command:
                case UiGlyph.CommandOutput:
                    return palette.GlyphIndigoColor;
                case UiGlyph.Account:
                    return palette.GlyphCyanColor;
                default:
                    return palette.GlyphNeutralColor;
            }
        }

        internal static UiThemeMode ResolveMode(string preference, bool isHighContrast, bool systemUsesDarkTheme)
        {
            if (isHighContrast)
            {
                return UiThemeMode.HighContrast;
            }

            var normalized = UiThemePreference.Normalize(preference);
            if (string.Equals(normalized, UiThemePreference.Light, StringComparison.OrdinalIgnoreCase))
            {
                return UiThemeMode.Light;
            }

            if (string.Equals(normalized, UiThemePreference.Dark, StringComparison.OrdinalIgnoreCase))
            {
                return UiThemeMode.Dark;
            }

            return systemUsesDarkTheme ? UiThemeMode.Dark : UiThemeMode.Light;
        }

        internal static UiThemePalette GetPalette(UiThemeMode mode)
        {
            switch (mode)
            {
                case UiThemeMode.HighContrast:
                    return CreateHighContrastPalette();
                case UiThemeMode.Dark:
                    return CreateDarkPalette();
                default:
                    return CreateLightPalette();
            }
        }

        internal static void SetCurrentMode(UiThemeMode mode)
        {
            _currentMode = mode;
        }

        internal static void SetCurrentModeForTesting(UiThemeMode mode)
        {
            _currentMode = mode;
        }

        private static UiThemePalette CreateLightPalette()
        {
            return new UiThemePalette
            {
                WindowBackColor = Color.FromArgb(244, 241, 232),
                PanelBackColor = Color.FromArgb(251, 250, 245),
                CardBackColor = Color.FromArgb(255, 253, 247),
                SubtleBackColor = Color.FromArgb(236, 232, 220),
                BorderColor = Color.FromArgb(122, 133, 142),
                ActiveBorderColor = Color.FromArgb(45, 106, 116),
                SelectedBackColor = Color.FromArgb(178, 211, 199),
                SelectedTextColor = Color.FromArgb(16, 58, 52),
                TextColor = Color.FromArgb(31, 41, 51),
                MutedTextColor = Color.FromArgb(88, 101, 111),
                DisabledTextColor = Color.FromArgb(132, 140, 144),
                AccentColor = Color.FromArgb(46, 111, 115),
                AccentSoftBackColor = Color.FromArgb(226, 239, 234),
                GlyphBlueColor = Color.FromArgb(36, 94, 156),
                GlyphRedColor = Color.FromArgb(168, 45, 68),
                GlyphGreenColor = Color.FromArgb(36, 117, 76),
                GlyphTealColor = Color.FromArgb(29, 117, 111),
                GlyphAmberColor = Color.FromArgb(141, 90, 18),
                GlyphIndigoColor = Color.FromArgb(91, 83, 183),
                GlyphCyanColor = Color.FromArgb(36, 122, 140),
                GlyphNeutralColor = Color.FromArgb(101, 113, 124),
                ChatProcessingColor = Color.FromArgb(166, 101, 13),
                ChatErrorBackColor = Color.FromArgb(252, 235, 236),
                ChatApprovalBackColor = Color.FromArgb(250, 243, 218),
                ChatCommandBackColor = Color.FromArgb(239, 237, 251),
                ChatFileChangeBackColor = Color.FromArgb(233, 245, 236),
                ChatAccountBackColor = Color.FromArgb(230, 243, 244),
                ChatSystemBackColor = Color.FromArgb(241, 241, 236),
                ChatProcessingBackColor = Color.FromArgb(250, 243, 228),
                ChatCodexBackColor = Color.FromArgb(234, 242, 249),
                ChatUserBackColor = Color.FromArgb(233, 244, 240),
                StatusReadyColor = Color.FromArgb(31, 118, 74),
                StatusProcessingColor = Color.FromArgb(151, 90, 18),
                StatusErrorColor = Color.FromArgb(181, 45, 55),
                ShadowColor = Color.FromArgb(26, 34, 29, 20),
                ShadowCornerColor = Color.FromArgb(36, 34, 29, 20),
                ShadowFadeColor = Color.FromArgb(0, 0, 0, 0)
            };
        }

        private static UiThemePalette CreateDarkPalette()
        {
            return new UiThemePalette
            {
                WindowBackColor = Color.FromArgb(7, 10, 15),
                PanelBackColor = Color.FromArgb(13, 17, 24),
                CardBackColor = Color.FromArgb(21, 27, 36),
                SubtleBackColor = Color.FromArgb(16, 22, 32),
                BorderColor = Color.FromArgb(92, 105, 125),
                ActiveBorderColor = Color.FromArgb(121, 199, 216),
                SelectedBackColor = Color.FromArgb(40, 91, 112),
                SelectedTextColor = Color.FromArgb(255, 255, 255),
                TextColor = Color.FromArgb(244, 247, 250),
                MutedTextColor = Color.FromArgb(196, 207, 219),
                DisabledTextColor = Color.FromArgb(142, 151, 163),
                AccentColor = Color.FromArgb(113, 197, 214),
                AccentSoftBackColor = Color.FromArgb(18, 41, 54),
                GlyphBlueColor = Color.FromArgb(130, 185, 255),
                GlyphRedColor = Color.FromArgb(255, 135, 157),
                GlyphGreenColor = Color.FromArgb(113, 226, 163),
                GlyphTealColor = Color.FromArgb(95, 225, 204),
                GlyphAmberColor = Color.FromArgb(255, 209, 102),
                GlyphIndigoColor = Color.FromArgb(184, 181, 255),
                GlyphCyanColor = Color.FromArgb(125, 230, 255),
                GlyphNeutralColor = Color.FromArgb(169, 183, 200),
                ChatProcessingColor = Color.FromArgb(255, 190, 92),
                ChatErrorBackColor = Color.FromArgb(59, 17, 24),
                ChatApprovalBackColor = Color.FromArgb(58, 43, 8),
                ChatCommandBackColor = Color.FromArgb(37, 33, 75),
                ChatFileChangeBackColor = Color.FromArgb(14, 52, 36),
                ChatAccountBackColor = Color.FromArgb(11, 51, 64),
                ChatSystemBackColor = Color.FromArgb(21, 27, 36),
                ChatProcessingBackColor = Color.FromArgb(59, 40, 11),
                ChatCodexBackColor = Color.FromArgb(16, 43, 74),
                ChatUserBackColor = Color.FromArgb(13, 51, 49),
                StatusReadyColor = Color.FromArgb(103, 230, 160),
                StatusProcessingColor = Color.FromArgb(255, 209, 102),
                StatusErrorColor = Color.FromArgb(255, 128, 150),
                ShadowColor = Color.FromArgb(32, 0, 0, 0),
                ShadowCornerColor = Color.FromArgb(48, 0, 0, 0),
                ShadowFadeColor = Color.FromArgb(0, 0, 0, 0)
            };
        }

        private static UiThemePalette CreateHighContrastPalette()
        {
            return new UiThemePalette
            {
                WindowBackColor = SystemColors.Control,
                PanelBackColor = SystemColors.Control,
                CardBackColor = SystemColors.Window,
                SubtleBackColor = SystemColors.Control,
                BorderColor = SystemColors.ControlDark,
                ActiveBorderColor = SystemColors.Highlight,
                SelectedBackColor = SystemColors.Highlight,
                SelectedTextColor = SystemColors.HighlightText,
                TextColor = SystemColors.ControlText,
                MutedTextColor = SystemColors.ControlText,
                DisabledTextColor = SystemColors.GrayText,
                AccentColor = SystemColors.Highlight,
                AccentSoftBackColor = SystemColors.Window,
                GlyphBlueColor = SystemColors.ControlText,
                GlyphRedColor = SystemColors.ControlText,
                GlyphGreenColor = SystemColors.ControlText,
                GlyphTealColor = SystemColors.ControlText,
                GlyphAmberColor = SystemColors.ControlText,
                GlyphIndigoColor = SystemColors.ControlText,
                GlyphCyanColor = SystemColors.ControlText,
                GlyphNeutralColor = SystemColors.ControlText,
                ChatProcessingColor = SystemColors.Highlight,
                ChatErrorBackColor = SystemColors.Window,
                ChatApprovalBackColor = SystemColors.Window,
                ChatCommandBackColor = SystemColors.Window,
                ChatFileChangeBackColor = SystemColors.Window,
                ChatAccountBackColor = SystemColors.Window,
                ChatSystemBackColor = SystemColors.Window,
                ChatProcessingBackColor = SystemColors.Window,
                ChatCodexBackColor = SystemColors.Window,
                ChatUserBackColor = SystemColors.Window,
                StatusReadyColor = SystemColors.HighlightText,
                StatusProcessingColor = SystemColors.HighlightText,
                StatusErrorColor = SystemColors.HighlightText,
                ShadowColor = SystemColors.Window,
                ShadowCornerColor = SystemColors.Window,
                ShadowFadeColor = SystemColors.Window
            };
        }

        private static void ApplyControl(Control control)
        {
            var form = control as Form;
            if (form != null)
            {
                ApplyForm(form);
                return;
            }

            var statusStrip = control as StatusStrip;
            if (statusStrip != null)
            {
                ApplyStatusStrip(statusStrip);
                return;
            }

            var toolStrip = control as ToolStrip;
            if (toolStrip != null)
            {
                ApplyToolStrip(toolStrip);
                return;
            }

            if (TryApplySpecializedControl(control))
            {
                return;
            }

            var label = control as Label;
            if (label != null)
            {
                ApplyLabel(label, IsMutedLabelColor(label.ForeColor));
                return;
            }

            var button = control as Button;
            if (button != null)
            {
                ApplyButtonSurface(button);
                return;
            }

            var splitContainer = control as SplitContainer;
            if (splitContainer != null)
            {
                ApplySplitContainer(splitContainer);
                return;
            }

            if (control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel)
            {
                control.BackColor = MapSurfaceColor(control.BackColor);
                control.ForeColor = TextColor;
                ApplyScrollBarTheme((ScrollableControl)control);
                return;
            }

            if (!(control is Form))
            {
                control.ForeColor = TextColor;
                if (control.BackColor != Color.Transparent)
                {
                    control.BackColor = MapSurfaceColor(control.BackColor);
                }
            }
        }

        // Auto-scroll containers draw native scroll bars, which stay light unless the window gets the dark theme.
        private static void ApplyScrollBarTheme(ScrollableControl control)
        {
            if (control.AutoScroll)
            {
                NativeWindowTheme.Apply(control, _currentMode == UiThemeMode.Dark);
            }
        }

        private static Color MapSurfaceColor(Color current)
        {
            if (current == Color.Transparent)
            {
                return current;
            }

            if (MatchesColor(current, CreateLightPalette().PanelBackColor, CreateDarkPalette().PanelBackColor))
            {
                return PanelBackColor;
            }

            if (MatchesColor(current, CreateLightPalette().CardBackColor, CreateDarkPalette().CardBackColor, SystemColors.Window))
            {
                return CardBackColor;
            }

            if (MatchesColor(current, CreateLightPalette().SubtleBackColor, CreateDarkPalette().SubtleBackColor))
            {
                return SubtleBackColor;
            }

            return WindowBackColor;
        }

        private static bool MatchesColor(Color current, params Color[] colors)
        {
            foreach (var color in colors)
            {
                if (current.ToArgb() == color.ToArgb())
                {
                    return true;
                }
            }

            return false;
        }

        private static Color GetGlyphContrastColor(UiThemeMode mode)
        {
            if (IsHighContrast)
            {
                return SystemColors.Window;
            }

            return GetPalette(mode).CardBackColor;
        }

        private static bool IsCodexSender(string sender)
        {
            return string.Equals(sender, "codex", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sender, "Codex", StringComparison.OrdinalIgnoreCase);
        }

    }
}
