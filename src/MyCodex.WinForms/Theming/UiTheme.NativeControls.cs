using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal static partial class UiTheme
    {
        private static readonly ConditionalWeakTable<ComboBox, ComboBoxThemeState> ComboBoxStates =
            new ConditionalWeakTable<ComboBox, ComboBoxThemeState>();
        private static readonly ConditionalWeakTable<ListBox, ListBoxThemeState> ListBoxStates =
            new ConditionalWeakTable<ListBox, ListBoxThemeState>();
        private static readonly ConditionalWeakTable<ListView, ListViewThemeState> ListViewStates =
            new ConditionalWeakTable<ListView, ListViewThemeState>();
        private static readonly ConditionalWeakTable<DateTimePicker, object> DateTimePickerStates =
            new ConditionalWeakTable<DateTimePicker, object>();
        private static readonly ConditionalWeakTable<TreeView, TreeViewThemeState> TreeViewStates =
            new ConditionalWeakTable<TreeView, TreeViewThemeState>();
        private static readonly ConditionalWeakTable<ButtonBase, ToggleThemeState> ToggleStates =
            new ConditionalWeakTable<ButtonBase, ToggleThemeState>();
        private static readonly ConditionalWeakTable<GroupBox, object> GroupBoxStates =
            new ConditionalWeakTable<GroupBox, object>();
        private static readonly ConditionalWeakTable<SplitContainer, object> SplitContainerStates =
            new ConditionalWeakTable<SplitContainer, object>();

        public static void ApplyStatusStrip(StatusStrip statusStrip)
        {
            if (statusStrip == null)
            {
                return;
            }

            statusStrip.Font = CreateUiFont(BodyFontSize, FontStyle.Regular);
            statusStrip.BackColor = PanelBackColor;
            statusStrip.ForeColor = TextColor;
            if (_currentMode == UiThemeMode.HighContrast)
            {
                statusStrip.RenderMode = ToolStripRenderMode.System;
            }
            else
            {
                statusStrip.Renderer = new ThemedToolStripRenderer();
            }

            ApplyToolStripItems(statusStrip.Items);

            statusStrip.Refresh();
        }

        public static void ApplyToolStrip(ToolStrip toolStrip)
        {
            if (toolStrip == null)
            {
                return;
            }

            toolStrip.BackColor = PanelBackColor;
            toolStrip.ForeColor = TextColor;
            if (_currentMode == UiThemeMode.HighContrast)
            {
                toolStrip.RenderMode = ToolStripRenderMode.System;
            }
            else
            {
                toolStrip.Renderer = new ThemedToolStripRenderer();
            }

            ApplyToolStripItems(toolStrip.Items);

            toolStrip.Refresh();
        }

        private static bool TryApplySpecializedControl(Control control)
        {
            var commandTerminalOutput = control as Forms.CommandTerminalOutputControl;
            if (commandTerminalOutput != null)
            {
                commandTerminalOutput.ApplyTheme();
                return true;
            }

            var tabControl = control as TabControl;
            if (tabControl != null)
            {
                ApplyTabControl(tabControl);
                return true;
            }

            var tabPage = control as TabPage;
            if (tabPage != null)
            {
                ApplyTabPage(tabPage);
                return true;
            }

            var listView = control as ListView;
            if (listView != null)
            {
                ApplyListView(listView);
                return true;
            }

            var listBox = control as ListBox;
            if (listBox != null)
            {
                ApplyListBox(listBox);
                return true;
            }

            var treeView = control as TreeView;
            if (treeView != null)
            {
                ApplyTreeView(treeView);
                return true;
            }

            var upDown = control as UpDownBase;
            if (upDown != null)
            {
                ApplyUpDown(upDown);
                return true;
            }

            var dateTimePicker = control as DateTimePicker;
            if (dateTimePicker != null)
            {
                ApplyDateTimePicker(dateTimePicker);
                return true;
            }

            var progressBar = control as ProgressBar;
            if (progressBar != null)
            {
                progressBar.Invalidate();
                return true;
            }

            var groupBox = control as GroupBox;
            if (groupBox != null)
            {
                ApplyGroupBox(groupBox);
                return true;
            }

            var comboBox = control as ComboBox;
            if (comboBox != null)
            {
                ApplyComboBox(comboBox);
                return true;
            }

            var checkBox = control as CheckBox;
            if (checkBox != null)
            {
                ApplyCheckBox(checkBox);
                return true;
            }

            var radioButton = control as RadioButton;
            if (radioButton != null)
            {
                ApplyRadioButton(radioButton);
                return true;
            }

            var textBox = control as TextBox;
            if (textBox != null)
            {
                ApplyTextBox(textBox, IsCodeFont(textBox.Font), textBox.ReadOnly);
                return true;
            }

            var richTextBox = control as RichTextBox;
            if (richTextBox != null)
            {
                ApplyRichTextBox(richTextBox);
                return true;
            }

            return false;
        }

        private static void ApplyComboBoxTheme(ComboBox comboBox)
        {
            ComboBoxThemeState state;
            if (!ComboBoxStates.TryGetValue(comboBox, out state))
            {
                state = new ComboBoxThemeState
                {
                    DrawMode = comboBox.DrawMode,
                    FlatStyle = comboBox.FlatStyle,
                    ItemHeight = comboBox.ItemHeight
                };
                ComboBoxStates.Add(comboBox, state);
                comboBox.DrawItem += DrawComboBoxItem;
                comboBox.Paint += PaintComboBoxSurface;
                comboBox.DropDown += (s, e) => NativeWindowTheme.Apply(comboBox, _currentMode == UiThemeMode.Dark);
            }

            NativeWindowTheme.Apply(comboBox, _currentMode == UiThemeMode.Dark);
            if (_currentMode == UiThemeMode.HighContrast)
            {
                comboBox.DrawMode = state.DrawMode;
                comboBox.FlatStyle = state.FlatStyle;
                comboBox.BackColor = SystemColors.Window;
                comboBox.ForeColor = TextColor;
                comboBox.ItemHeight = state.ItemHeight;
            }
            else
            {
                comboBox.DrawMode = DrawMode.OwnerDrawFixed;
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.BackColor = CardBackColor;
                comboBox.ForeColor = TextColor;
                comboBox.ItemHeight = Math.Max(
                    state.ItemHeight,
                    TextRenderer.MeasureText("Ag", comboBox.Font).Height + Scale(6));
            }

            comboBox.Invalidate();
        }

        private static void ApplyToggleTheme(ButtonBase control, bool radio)
        {
            ToggleThemeState state;
            if (!ToggleStates.TryGetValue(control, out state))
            {
                state = new ToggleThemeState
                {
                    BackColor = control.BackColor,
                    UseVisualStyleBackColor = control.UseVisualStyleBackColor
                };
                ToggleStates.Add(control, state);
                control.MouseEnter += (s, e) => SetToggleHovered(control, state, true);
                control.MouseLeave += (s, e) => SetToggleHovered(control, state, false);
                control.EnabledChanged += (s, e) => control.Invalidate();
                control.GotFocus += (s, e) => control.Invalidate();
                control.LostFocus += (s, e) => control.Invalidate();
                control.TextChanged += (s, e) => control.Invalidate();
            }

            state.Radio = radio;
            if (_currentMode == UiThemeMode.HighContrast)
            {
                control.BackColor = state.BackColor;
                control.ForeColor = TextColor;
                control.UseVisualStyleBackColor = state.UseVisualStyleBackColor;
            }
            else
            {
                control.BackColor = control.Parent != null && control.Parent.BackColor != Color.Transparent
                    ? control.Parent.BackColor
                    : WindowBackColor;
                control.ForeColor = TextColor;
                control.UseVisualStyleBackColor = false;
            }

            control.Invalidate();
        }

        private static void SetToggleHovered(ButtonBase control, ToggleThemeState state, bool hovered)
        {
            state.Hovered = hovered;
            control.Invalidate();
        }

        private static void PaintComboBoxSurface(object sender, PaintEventArgs e)
        {
            var comboBox = sender as ComboBox;
            if (comboBox == null)
            {
                return;
            }

            PaintComboBoxChrome(comboBox, e.Graphics);
        }

        internal static void PaintComboBoxChrome(ComboBox comboBox)
        {
            if (comboBox == null || !comboBox.IsHandleCreated || _currentMode == UiThemeMode.HighContrast)
            {
                return;
            }

            using (var graphics = Graphics.FromHwnd(comboBox.Handle))
            {
                PaintComboBoxChrome(comboBox, graphics);
            }
        }

        internal static void PaintComboBoxChrome(ComboBox comboBox, Graphics graphics)
        {
            if (_currentMode == UiThemeMode.HighContrast)
            {
                return;
            }

            Rectangle itemBounds;
            Rectangle buttonBounds;
            if (!NativeWindowTheme.TryGetComboBoxParts(comboBox, out itemBounds, out buttonBounds))
            {
                return;
            }

            // The flat native frame paints the ring around the field and the gap before the button
            // in system colors (white when disabled), so cover everything except the item text.
            using (var surface = new Region(Rectangle.Inflate(new Rectangle(Point.Empty, comboBox.ClientSize), -1, -1)))
            using (var surfaceBrush = new SolidBrush(CardBackColor))
            {
                if (comboBox.DropDownStyle == ComboBoxStyle.DropDownList)
                {
                    surface.Exclude(itemBounds);
                }
                else
                {
                    surface.Exclude(Rectangle.FromLTRB(itemBounds.Left, itemBounds.Top, buttonBounds.Left, itemBounds.Bottom));
                }

                surface.Exclude(buttonBounds);
                graphics.FillRegion(surfaceBrush, surface);
            }

            var buttonBack = comboBox.DroppedDown
                ? AccentSoftBackColor
                : comboBox.Enabled ? CardBackColor : SubtleBackColor;
            using (var backgroundBrush = new SolidBrush(buttonBack))
            using (var borderPen = new Pen(comboBox.Enabled && comboBox.Focused ? ActiveBorderColor : BorderColor))
            {
                graphics.FillRectangle(backgroundBrush, buttonBounds);
                graphics.DrawLine(borderPen, buttonBounds.Left, buttonBounds.Top + 2, buttonBounds.Left, buttonBounds.Bottom - 2);
                graphics.DrawRectangle(
                    borderPen,
                    0,
                    0,
                    Math.Max(0, comboBox.ClientSize.Width - 1),
                    Math.Max(0, comboBox.ClientSize.Height - 1));
            }

            var arrowColor = comboBox.Enabled ? TextColor : DisabledTextColor;
            var centerX = buttonBounds.Left + buttonBounds.Width / 2;
            var centerY = buttonBounds.Top + buttonBounds.Height / 2;
            using (var arrowPen = new Pen(arrowColor, Math.Max(1.4F, (float)Scale(2))))
            {
                graphics.DrawLines(arrowPen, new[]
                {
                    new Point(centerX - Scale(4), centerY - Scale(2)),
                    new Point(centerX, centerY + Scale(2)),
                    new Point(centerX + Scale(4), centerY - Scale(2))
                });
            }
        }

        private static void DrawComboBoxItem(object sender, DrawItemEventArgs e)
        {
            var comboBox = sender as ComboBox;
            if (comboBox == null || _currentMode == UiThemeMode.HighContrast)
            {
                return;
            }

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var background = selected && comboBox.Enabled ? SelectedBackColor : CardBackColor;
            var foreground = !comboBox.Enabled
                ? DisabledTextColor
                : selected ? SelectedTextColor : TextColor;
            using (var backgroundBrush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
            }
            if (selected)
            {
                using (var accentBrush = new SolidBrush(ActiveBorderColor))
                {
                    e.Graphics.FillRectangle(accentBrush, e.Bounds.Left, e.Bounds.Top, Scale(4), e.Bounds.Height);
                }
            }

            var text = e.Index >= 0 && e.Index < comboBox.Items.Count
                ? comboBox.GetItemText(comboBox.Items[e.Index])
                : comboBox.Text;
            var textBounds = Rectangle.Inflate(e.Bounds, -Scale(6), 0);
            TextRenderer.DrawText(
                e.Graphics,
                text,
                e.Font,
                textBounds,
                foreground,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) == DrawItemState.Focus && comboBox.Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, textBounds, foreground, background);
            }
        }

        private static void ApplyListBoxTheme(ListBox listBox)
        {
            ListBoxThemeState state;
            if (!ListBoxStates.TryGetValue(listBox, out state))
            {
                state = new ListBoxThemeState
                {
                    DrawMode = listBox.DrawMode,
                    ItemHeight = listBox.ItemHeight
                };
                ListBoxStates.Add(listBox, state);
                if (state.DrawMode == DrawMode.Normal)
                {
                    state.SupportsThemeDrawing = true;
                    listBox.DrawItem += DrawListBoxItem;
                }
            }

            if (!state.SupportsThemeDrawing)
            {
                return;
            }

            if (_currentMode == UiThemeMode.HighContrast)
            {
                listBox.DrawMode = state.DrawMode;
                listBox.ItemHeight = state.ItemHeight;
            }
            else
            {
                listBox.DrawMode = DrawMode.OwnerDrawFixed;
                listBox.ItemHeight = Math.Max(
                    state.ItemHeight,
                    TextRenderer.MeasureText("Ag", listBox.Font).Height + Scale(6));
            }

            listBox.Invalidate();
        }

        private static void DrawListBoxItem(object sender, DrawItemEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox == null || _currentMode == UiThemeMode.HighContrast)
            {
                return;
            }

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var background = selected ? SelectedBackColor : CardBackColor;
            var foreground = !listBox.Enabled
                ? MutedTextColor
                : selected ? SelectedTextColor : TextColor;
            using (var backgroundBrush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
            }
            if (selected)
            {
                using (var accentBrush = new SolidBrush(ActiveBorderColor))
                {
                    e.Graphics.FillRectangle(accentBrush, e.Bounds.Left, e.Bounds.Top, Scale(4), e.Bounds.Height);
                }
            }

            var textLeft = e.Bounds.Left + Scale(5);
            var checkedListBox = listBox as CheckedListBox;
            if (checkedListBox != null && e.Index >= 0 && e.Index < checkedListBox.Items.Count)
            {
                var checkBounds = new Rectangle(
                    textLeft,
                    e.Bounds.Top + Math.Max(1, (e.Bounds.Height - Scale(13)) / 2),
                    Scale(13),
                    Scale(13));
                DrawListCheckBox(e.Graphics, checkBounds, checkedListBox.GetItemChecked(e.Index));
                textLeft = checkBounds.Right + Scale(5);
            }

            var text = e.Index >= 0 && e.Index < listBox.Items.Count
                ? listBox.GetItemText(listBox.Items[e.Index])
                : listBox.Text;
            var textBounds = new Rectangle(
                textLeft,
                e.Bounds.Top,
                Math.Max(0, e.Bounds.Right - textLeft),
                e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                text,
                e.Font,
                textBounds,
                foreground,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) == DrawItemState.Focus && listBox.Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, textBounds, foreground, background);
            }
        }

        private static void ApplyTabControl(TabControl tabControl)
        {
            tabControl.BackColor = PanelBackColor;
            tabControl.ForeColor = TextColor;
            foreach (TabPage page in tabControl.TabPages)
            {
                ApplyTabPage(page);
            }

            if (!(tabControl is ThemedTabControl))
            {
                NativeWindowTheme.Apply(tabControl, _currentMode == UiThemeMode.Dark);
            }

            tabControl.Invalidate();
        }

        private static void ApplyTabPage(TabPage tabPage)
        {
            tabPage.BackColor = WindowBackColor;
            tabPage.ForeColor = TextColor;
            tabPage.UseVisualStyleBackColor = _currentMode != UiThemeMode.Dark;
            tabPage.Invalidate();
        }

        internal static void ApplyTreeView(TreeView treeView)
        {
            TreeViewThemeState state;
            if (!TreeViewStates.TryGetValue(treeView, out state))
            {
                state = new TreeViewThemeState { DrawMode = treeView.DrawMode };
                TreeViewStates.Add(treeView, state);
                treeView.DrawNode += DrawTreeNode;
            }

            treeView.BorderStyle = BorderStyle.FixedSingle;
            NativeWindowTheme.Apply(treeView, _currentMode == UiThemeMode.Dark);
            treeView.BackColor = CardBackColor;
            treeView.ForeColor = TextColor;
            treeView.LineColor = BorderColor;
            // Repainting is enough; collapsing and re-expanding nodes would raise selection and expansion events.
            treeView.DrawMode = _currentMode == UiThemeMode.HighContrast
                ? state.DrawMode
                : TreeViewDrawMode.OwnerDrawAll;
            treeView.Invalidate();
        }

        // OwnerDrawAll passes the whole row, so the native label position supplies the level indent.
        private static int GetTreeNodeLabelLeft(TreeView treeView, TreeNode node, Rectangle rowBounds)
        {
            var label = node.Bounds;
            if (label.Width > 0)
            {
                return label.Left;
            }

            var slots = node.Level + (treeView.ShowRootLines ? 1 : 0);
            var imageWidth = treeView.ImageList == null ? 0 : treeView.ImageList.ImageSize.Width;
            return rowBounds.Left + slots * treeView.Indent + imageWidth;
        }

        private static void DrawTreeNode(object sender, DrawTreeNodeEventArgs e)
        {
            var treeView = sender as TreeView;
            if (treeView == null || e.Node == null || _currentMode == UiThemeMode.HighContrast)
            {
                e.DrawDefault = true;
                return;
            }

            var selected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected || e.Node.IsSelected;
            var background = selected ? SelectedBackColor : CardBackColor;
            var foreground = !treeView.Enabled
                ? MutedTextColor
                : selected ? SelectedTextColor : TextColor;
            using (var backgroundBrush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
            }
            if (selected)
            {
                using (var accentBrush = new SolidBrush(ActiveBorderColor))
                {
                    e.Graphics.FillRectangle(accentBrush, e.Bounds.Left, e.Bounds.Top, Scale(4), e.Bounds.Height);
                }
            }

            var textLeft = GetTreeNodeLabelLeft(treeView, e.Node, e.Bounds);
            var imageList = treeView.ImageList;
            var imageLeft = imageList == null ? textLeft : textLeft - imageList.ImageSize.Width;
            var hasGlyph = e.Node.Nodes.Count > 0 && treeView.ShowPlusMinus && (e.Node.Level > 0 || treeView.ShowRootLines);
            if (hasGlyph)
            {
                // Centre the glyph in the indent slot where the native control hit-tests it.
                var glyphSize = Math.Min(Scale(11), Math.Max(7, treeView.Indent - 4));
                var glyphBounds = new Rectangle(
                    imageLeft - treeView.Indent + (treeView.Indent - glyphSize) / 2,
                    e.Bounds.Top + Math.Max(1, (e.Bounds.Height - glyphSize) / 2),
                    glyphSize,
                    glyphSize);
                using (var glyphPen = new Pen(foreground))
                {
                    e.Graphics.DrawRectangle(glyphPen, glyphBounds.Left, glyphBounds.Top, glyphBounds.Width - 1, glyphBounds.Height - 1);
                    var middleY = glyphBounds.Top + glyphBounds.Height / 2;
                    e.Graphics.DrawLine(glyphPen, glyphBounds.Left + Scale(3), middleY, glyphBounds.Right - Scale(3), middleY);
                    if (!e.Node.IsExpanded)
                    {
                        var middleX = glyphBounds.Left + glyphBounds.Width / 2;
                        e.Graphics.DrawLine(glyphPen, middleX, glyphBounds.Top + Scale(3), middleX, glyphBounds.Bottom - Scale(3));
                    }
                }
            }

            if (imageList != null && e.Node.ImageIndex >= 0 && e.Node.ImageIndex < imageList.Images.Count)
            {
                var image = imageList.Images[e.Node.ImageIndex];
                e.Graphics.DrawImage(
                    image,
                    imageLeft,
                    e.Bounds.Top + Math.Max(1, (e.Bounds.Height - image.Height) / 2),
                    image.Width,
                    image.Height);
            }

            var textBounds = new Rectangle(
                textLeft,
                e.Bounds.Top,
                Math.Max(0, e.Bounds.Right - textLeft),
                e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                e.Node.Text,
                treeView.Font,
                textBounds,
                foreground,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            if ((e.State & TreeNodeStates.Focused) == TreeNodeStates.Focused && treeView.Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -Scale(1), -Scale(2)), foreground, background);
            }
        }

        private static void ApplyUpDown(UpDownBase upDown)
        {
            NativeWindowTheme.Apply(upDown, _currentMode == UiThemeMode.Dark);
            upDown.BackColor = _currentMode == UiThemeMode.HighContrast ? SystemColors.Window : CardBackColor;
            upDown.ForeColor = TextColor;
        }

        private static void ApplyDateTimePicker(DateTimePicker picker)
        {
            if (!DateTimePickerStates.TryGetValue(picker, out _))
            {
                DateTimePickerStates.Add(picker, new object());
                picker.DropDown += (s, e) => NativeWindowTheme.Apply(picker, _currentMode == UiThemeMode.Dark);
            }

            var dark = _currentMode == UiThemeMode.Dark;
            var highContrast = _currentMode == UiThemeMode.HighContrast;
            NativeWindowTheme.Apply(picker, dark);
            picker.BackColor = highContrast ? SystemColors.Window : CardBackColor;
            picker.ForeColor = highContrast ? SystemColors.WindowText : TextColor;
            picker.CalendarMonthBackground = highContrast ? SystemColors.Window : CardBackColor;
            picker.CalendarForeColor = highContrast ? SystemColors.WindowText : TextColor;
            picker.CalendarTitleBackColor = highContrast ? SystemColors.ActiveCaption : PanelBackColor;
            picker.CalendarTitleForeColor = highContrast ? SystemColors.ActiveCaptionText : TextColor;
            picker.CalendarTrailingForeColor = highContrast ? SystemColors.GrayText : MutedTextColor;
        }

        private static void ApplyRichTextBox(RichTextBox richTextBox)
        {
            NativeWindowTheme.Apply(richTextBox, _currentMode == UiThemeMode.Dark);
            richTextBox.BackColor = CardBackColor;
            richTextBox.ForeColor = TextColor;
            richTextBox.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void ApplyGroupBox(GroupBox groupBox)
        {
            if (!GroupBoxStates.TryGetValue(groupBox, out _))
            {
                GroupBoxStates.Add(groupBox, new object());
                groupBox.Paint += PaintGroupBox;
            }

            groupBox.BackColor = groupBox.Parent != null && groupBox.Parent.BackColor != Color.Transparent
                ? groupBox.Parent.BackColor
                : WindowBackColor;
            groupBox.ForeColor = TextColor;
            groupBox.Invalidate();
        }

        private static void ApplySplitContainer(SplitContainer splitContainer)
        {
            if (!SplitContainerStates.TryGetValue(splitContainer, out _))
            {
                SplitContainerStates.Add(splitContainer, new object());
                splitContainer.Paint += (s, e) =>
                {
                    UiTheme.DrawSplitterGrip(e.Graphics, splitContainer.SplitterRectangle, splitContainer.Orientation);
                };
            }

            splitContainer.BackColor = MapSurfaceColor(splitContainer.BackColor);
            splitContainer.ForeColor = TextColor;
            splitContainer.Panel1.BackColor = splitContainer.BackColor;
            splitContainer.Panel2.BackColor = splitContainer.BackColor;
            splitContainer.Invalidate(true);
        }

        private static void PaintGroupBox(object sender, PaintEventArgs e)
        {
            var groupBox = sender as GroupBox;
            if (groupBox == null || _currentMode == UiThemeMode.HighContrast)
            {
                return;
            }

            using (var backgroundBrush = new SolidBrush(groupBox.BackColor))
            using (var borderPen = new Pen(BorderColor))
            {
                e.Graphics.FillRectangle(backgroundBrush, groupBox.ClientRectangle);
                var top = Math.Max(1, groupBox.Font.Height / 2);
                e.Graphics.DrawLine(borderPen, 0, top, groupBox.ClientSize.Width - 1, top);
                e.Graphics.DrawLine(borderPen, 0, top, 0, groupBox.ClientSize.Height - 1);
                e.Graphics.DrawLine(borderPen, groupBox.ClientSize.Width - 1, top, groupBox.ClientSize.Width - 1, groupBox.ClientSize.Height - 1);
                e.Graphics.DrawLine(borderPen, 0, groupBox.ClientSize.Height - 1, groupBox.ClientSize.Width - 1, groupBox.ClientSize.Height - 1);
            }

            var captionBounds = new Rectangle(Scale(8), 0, Math.Max(0, groupBox.ClientSize.Width - Scale(16)), groupBox.Font.Height + Scale(2));
            using (var backgroundBrush = new SolidBrush(groupBox.BackColor))
            {
                e.Graphics.FillRectangle(backgroundBrush, captionBounds);
            }

            TextRenderer.DrawText(
                e.Graphics,
                groupBox.Text,
                groupBox.Font,
                captionBounds,
                TextColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine);
        }

        private static Color GetEditableTextBackColor()
        {
            if (_currentMode == UiThemeMode.HighContrast)
            {
                return SystemColors.Window;
            }

            return CardBackColor;
        }

        private static bool IsCodeFont(Font font)
        {
            return font != null && string.Equals(font.Name, "Consolas", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMutedLabelColor(Color color)
        {
            return color.ToArgb() == CreateLightPalette().MutedTextColor.ToArgb() ||
                color.ToArgb() == CreateDarkPalette().MutedTextColor.ToArgb();
        }

        private static void ApplyListViewTheme(ListView listView)
        {
            ListViewThemeState state;
            if (!ListViewStates.TryGetValue(listView, out state))
            {
                state = new ListViewThemeState
                {
                    OwnerDraw = listView.OwnerDraw,
                    GridLines = listView.GridLines
                };
                ListViewStates.Add(listView, state);
                listView.DrawItem += DrawListViewItem;
                listView.DrawColumnHeader += DrawListViewColumnHeader;
                listView.Resize += (s, e) => ScheduleListViewTrailingColumn(listView, state);
                listView.ColumnWidthChanged += (s, e) =>
                {
                    if (!state.Filling)
                    {
                        ScheduleListViewTrailingColumn(listView, state);
                    }
                };
            }

            if (_currentMode != UiThemeMode.HighContrast &&
                (listView.View == View.Details || listView.View == View.List))
            {
                listView.OwnerDraw = true;
                if (listView.View == View.Details)
                {
                    listView.GridLines = false;
                }
            }
            else
            {
                listView.OwnerDraw = state.OwnerDraw;
                listView.GridLines = state.GridLines;
            }

            ScheduleListViewTrailingColumn(listView, state);
        }

        private static void DrawListViewItem(object sender, DrawListViewItemEventArgs e)
        {
            var listView = sender as ListView;
            if (listView == null ||
                _currentMode == UiThemeMode.HighContrast ||
                listView.View != View.Details && listView.View != View.List)
            {
                e.DrawDefault = true;
                return;
            }

            ListViewThemeState state;
            if (!ListViewStates.TryGetValue(listView, out state))
            {
                e.DrawDefault = true;
                return;
            }

            var selected = e.Item.Selected;
            var background = selected ? SelectedBackColor : CardBackColor;
            var defaultForeground = selected ? SelectedTextColor : TextColor;
            using (var backgroundBrush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
            }
            if (selected)
            {
                using (var accentBrush = new SolidBrush(ActiveBorderColor))
                {
                    e.Graphics.FillRectangle(accentBrush, e.Bounds.Left, e.Bounds.Top, Scale(4), e.Bounds.Height);
                }
            }

            var textLeftInset = 0;
            if (listView.CheckBoxes)
            {
                var checkBounds = new Rectangle(
                    e.Bounds.Left + Scale(5),
                    e.Bounds.Top + Math.Max(1, (e.Bounds.Height - Scale(14)) / 2),
                    Scale(14),
                    Scale(14));
                DrawListCheckBox(e.Graphics, checkBounds, e.Item.Checked);
                textLeftInset += checkBounds.Width + Scale(6);
            }

            var imageList = e.Item.ImageList ?? listView.SmallImageList;
            if (imageList != null && e.Item.ImageIndex >= 0 && e.Item.ImageIndex < imageList.Images.Count)
            {
                var image = imageList.Images[e.Item.ImageIndex];
                e.Graphics.DrawImage(
                    image,
                    e.Bounds.Left + Scale(5) + textLeftInset,
                    e.Bounds.Top + Math.Max(1, (e.Bounds.Height - image.Height) / 2),
                    image.Width,
                    image.Height);
                textLeftInset += image.Width + Scale(6);
            }

            if (listView.View == View.List)
            {
                var textBounds = new Rectangle(
                    e.Bounds.Left + Scale(5) + textLeftInset,
                    e.Bounds.Top,
                    Math.Max(0, e.Bounds.Width - Scale(10) - textLeftInset),
                    e.Bounds.Height);
                var sourceColor = e.Item.ForeColor;
                var textColor = !listView.Enabled
                    ? MutedTextColor
                    : selected
                        ? SelectedTextColor
                        : ResolveListViewTextColor(sourceColor, defaultForeground);
                TextRenderer.DrawText(
                    e.Graphics,
                    e.Item.Text,
                    listView.Font,
                    textBounds,
                    textColor,
                    TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
            }
            else
            {
                for (var index = 0; index < listView.Columns.Count && index < e.Item.SubItems.Count; index++)
                {
                    var subItem = e.Item.SubItems[index];
                    var bounds = GetListViewCellBounds(listView, subItem, index);
                    if (bounds.Width <= 0)
                    {
                        continue;
                    }

                    var textBounds = bounds;
                    if (index == 0 && textLeftInset > 0)
                    {
                        textBounds = new Rectangle(
                            bounds.Left + textLeftInset,
                            bounds.Top,
                            Math.Max(0, bounds.Width - textLeftInset),
                            bounds.Height);
                    }

                    textBounds = Rectangle.Inflate(textBounds, -Scale(5), 0);
                    var sourceColor = e.Item.UseItemStyleForSubItems ? e.Item.ForeColor : subItem.ForeColor;
                    var textColor = !listView.Enabled
                        ? MutedTextColor
                        : selected
                            ? SelectedTextColor
                            : ResolveListViewTextColor(sourceColor, defaultForeground);
                    var flags = TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine |
                        TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPrefix;
                    switch (listView.Columns[index].TextAlign)
                    {
                        case HorizontalAlignment.Center:
                            flags |= TextFormatFlags.HorizontalCenter;
                            break;
                        case HorizontalAlignment.Right:
                            flags |= TextFormatFlags.Right;
                            break;
                        default:
                            flags |= TextFormatFlags.Left;
                            break;
                    }

                    TextRenderer.DrawText(e.Graphics, subItem.Text, listView.Font, textBounds, textColor, flags);
                    if (state.GridLines)
                    {
                        using (var gridPen = new Pen(BorderColor))
                        {
                            e.Graphics.DrawLine(gridPen, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
                            if (index < listView.Columns.Count - 1)
                            {
                                e.Graphics.DrawLine(gridPen, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
                            }
                        }
                    }
                }
            }

            if (e.Item.Focused && listView.Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -Scale(2), -Scale(2)), defaultForeground, background);
            }
        }

        // The first sub-item reports the whole row, so clip it to its own column.
        private static Rectangle GetListViewCellBounds(ListView listView, ListViewItem.ListViewSubItem subItem, int index)
        {
            var bounds = subItem.Bounds;
            return index == 0
                ? new Rectangle(bounds.Left, bounds.Top, Math.Min(bounds.Width, listView.Columns[0].Width), bounds.Height)
                : bounds;
        }

        private static void DrawListCheckBox(Graphics graphics, Rectangle bounds, bool isChecked)
        {
            using (var backgroundBrush = new SolidBrush(SubtleBackColor))
            using (var borderPen = new Pen(BorderColor))
            using (var checkPen = new Pen(TextColor, Math.Max(1.4F, (float)Scale(2))))
            {
                graphics.FillRectangle(backgroundBrush, bounds);
                graphics.DrawRectangle(borderPen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
                if (isChecked)
                {
                    graphics.DrawLines(checkPen, new[]
                    {
                        new Point(bounds.Left + Scale(3), bounds.Top + bounds.Height / 2),
                        new Point(bounds.Left + bounds.Width / 2, bounds.Bottom - Scale(4)),
                        new Point(bounds.Right - Scale(3), bounds.Top + Scale(3))
                    });
                }
            }
        }

        private static Color ResolveListViewTextColor(Color source, Color defaultColor)
        {
            if (source.ToArgb() == SystemColors.GrayText.ToArgb())
            {
                return MutedTextColor;
            }

            if (source.ToArgb() == Color.Firebrick.ToArgb() ||
                source.ToArgb() == Color.Red.ToArgb() ||
                source.ToArgb() == Color.DarkRed.ToArgb())
            {
                return GetPalette(_currentMode).StatusErrorColor;
            }

            return source.ToArgb() == SystemColors.WindowText.ToArgb() ||
                source.ToArgb() == SystemColors.ControlText.ToArgb()
                ? defaultColor
                : source;
        }

        private static void DrawListViewColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            if (_currentMode == UiThemeMode.HighContrast)
            {
                e.DrawDefault = true;
                return;
            }

            using (var backgroundBrush = new SolidBrush(SubtleBackColor))
            using (var borderPen = new Pen(BorderColor))
            {
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
                e.Graphics.DrawLine(borderPen, e.Bounds.Right - 1, e.Bounds.Top + 3, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
                e.Graphics.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            var flags = TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix;
            switch (e.Header.TextAlign)
            {
                case HorizontalAlignment.Center:
                    flags |= TextFormatFlags.HorizontalCenter;
                    break;
                case HorizontalAlignment.Right:
                    flags |= TextFormatFlags.Right;
                    break;
                default:
                    flags |= TextFormatFlags.Left;
                    break;
            }

            var textBounds = Rectangle.Inflate(e.Bounds, -Scale(6), 0);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, textBounds, TextColor, flags);
        }

        private static void FillListViewTrailingColumn(ListView listView, ListViewThemeState state)
        {
            if (state.Filling ||
                _currentMode == UiThemeMode.HighContrast ||
                listView.View != View.Details ||
                listView.Columns.Count == 0 ||
                listView.ClientSize.Width <= 0)
            {
                return;
            }

            var usedWidth = 0;
            for (var index = 0; index < listView.Columns.Count - 1; index++)
            {
                usedWidth += listView.Columns[index].Width;
            }

            var remainder = listView.ClientSize.Width - usedWidth;
            var lastColumn = listView.Columns[listView.Columns.Count - 1];
            var trailingBlankWidth = remainder - lastColumn.Width;
            if (trailingBlankWidth <= 0 || trailingBlankWidth > Scale(60))
            {
                return;
            }

            try
            {
                state.Filling = true;
                lastColumn.Width = remainder;
            }
            finally
            {
                state.Filling = false;
            }
        }

        private static void ScheduleListViewTrailingColumn(ListView listView, ListViewThemeState state)
        {
            if (state.FillScheduled ||
                state.Filling ||
                !listView.IsHandleCreated ||
                listView.IsDisposed ||
                _currentMode == UiThemeMode.HighContrast)
            {
                return;
            }

            state.FillScheduled = true;
            try
            {
                listView.BeginInvoke((MethodInvoker)delegate
                {
                    state.FillScheduled = false;
                    FillListViewTrailingColumn(listView, state);
                });
            }
            catch (InvalidOperationException)
            {
                state.FillScheduled = false;
            }
        }

        private sealed class ComboBoxThemeState
        {
            public DrawMode DrawMode { get; set; }

            public FlatStyle FlatStyle { get; set; }

            public int ItemHeight { get; set; }
        }

        private sealed class ListBoxThemeState
        {
            public DrawMode DrawMode { get; set; }

            public int ItemHeight { get; set; }

            public bool SupportsThemeDrawing { get; set; }
        }

        private sealed class ListViewThemeState
        {
            public bool OwnerDraw { get; set; }

            public bool GridLines { get; set; }

            public bool Filling { get; set; }

            public bool FillScheduled { get; set; }
        }

        private sealed class TreeViewThemeState
        {
            public TreeViewDrawMode DrawMode { get; set; }
        }

        private sealed class ToggleThemeState
        {
            public bool Radio { get; set; }

            public bool Hovered { get; set; }

            public Color BackColor { get; set; }

            public bool UseVisualStyleBackColor { get; set; }
        }
    }
}
