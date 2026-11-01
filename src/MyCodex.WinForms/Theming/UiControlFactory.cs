using System.Drawing;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    /// <summary>
    /// Creates controls that follow the dialog conventions shared by many forms. Property order matches the
    /// helpers these methods replaced, because auto-size buttons keep the largest size they have reached.
    /// </summary>
    internal static class UiControlFactory
    {
        /// <summary>
        /// Creates a fixed-width action button that is <see cref="UiTheme.ButtonHeight"/> tall, has a
        /// <see cref="UiTheme.ControlSpacing"/> left margin, and uses its mnemonic.
        /// </summary>
        internal static Button CreateActionButton(
            string text,
            string accessibleName,
            int tabIndex,
            int width,
            DialogResult dialogResult = DialogResult.None)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = width,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(UiTheme.ControlSpacing, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
            if (dialogResult != DialogResult.None)
            {
                button.DialogResult = dialogResult;
            }

            return button;
        }

        /// <summary>Creates a <see cref="CreateActionButton"/> style button that also keeps a small top margin.</summary>
        internal static Button CreateRaisedActionButton(string text, string accessibleName, int tabIndex, int width)
        {
            var button = CreateActionButton(text, accessibleName, tabIndex, width);
            button.Margin = new Padding(UiTheme.ControlSpacing, 4, 0, 0);
            return button;
        }

        /// <summary>Creates a fixed-size button with a right margin, for button rows that flow left to right.</summary>
        internal static Button CreateCompactButton(string text, string accessibleName, int width = 96, int tabIndex = 0)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = width,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(0, 0, UiTheme.ControlSpacing, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }

        /// <summary>Creates an auto-sized button with a minimum width, for the action rows of list windows.</summary>
        internal static Button CreateListActionButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                MinimumSize = new Size(108, 0),
                Margin = new Padding(0, 0, UiTheme.ControlSpacing, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }

        /// <summary>
        /// Creates an auto-sized dialog button with a glyph, styled by <see cref="UiTheme.ApplyDialogButton"/>.
        /// </summary>
        internal static Button CreateDialogButton(
            string text,
            string accessibleName,
            int tabIndex,
            UiGlyph glyph,
            DialogResult dialogResult = DialogResult.None)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                TabIndex = tabIndex,
                UseMnemonic = true
            };
            if (dialogResult != DialogResult.None)
            {
                button.DialogResult = dialogResult;
            }

            UiTheme.ApplyDialogButton(button, glyph);
            return button;
        }

        /// <summary>Creates a display-only muted label that fills its cell and centers its text vertically.</summary>
        internal static Label CreateMutedLabel(string text)
        {
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        /// <summary>Creates a display-only muted label that fills its cell and sizes to its text.</summary>
        internal static Label CreateFillAutoSizeLabel(string text)
        {
            var label = new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                AutoSize = true,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        /// <summary>Creates a themed single-line text box that fills its cell.</summary>
        internal static TextBox CreateFillTextBox(string accessibleName, int tabIndex)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, false, false);
            return textBox;
        }

        /// <summary>Creates a themed drop-down list.</summary>
        internal static ComboBox CreateDropDownList(string accessibleName, int tabIndex, DockStyle dock)
        {
            var comboBox = new ComboBox
            {
                Dock = dock,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
            UiTheme.ApplyComboBox(comboBox);
            return comboBox;
        }

        /// <summary>
        /// Creates a right-to-left, non-wrapping, auto-sized footer panel for dialog buttons. Its back color is
        /// inherited from the parent; use the overload with a color when the footer must paint its own background.
        /// </summary>
        internal static FlowLayoutPanel CreateDialogButtonBar()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                TabStop = false
            };
        }

        /// <summary>Creates a dialog button footer panel with an explicit back color.</summary>
        internal static FlowLayoutPanel CreateDialogButtonBar(Color backColor)
        {
            var panel = CreateDialogButtonBar();
            panel.BackColor = backColor;
            return panel;
        }
    }
}
