using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class CustomCommandPrefixEditForm : Form
    {
        private readonly TextBox _prefixTextBox;

        private CustomCommandPrefixEditForm(string prefix)
        {
            Text = UiLocalization.Text("CustomCommandPrefixEditForm.Title", "Custom Command Prefix");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 120);
            UiTheme.ApplyForm(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var label = new Label
            {
                Text = UiLocalization.Text("CustomCommandPrefixEditForm.Label.Prefix", "Command prefix(&P)"),
                AutoSize = true,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);

            _prefixTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("CustomCommandPrefixEditForm.Accessible.Prefix", "Command prefix"),
                Dock = DockStyle.Top,
                Text = prefix ?? string.Empty,
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_prefixTextBox, false, false);
            label.UseMnemonic = true;

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                TabStop = false
            };
            var okButton = new Button
            {
                Text = UiLocalization.Text("Common.OK", "&OK"),
                AccessibleName = UiLocalization.Text("Common.Accessible.OK", "OK"),
                AutoSize = true,
                DialogResult = DialogResult.OK,
                TabIndex = 1
            };
            UiTheme.ApplyDialogButton(okButton, UiGlyph.Ok);
            var cancelButton = new Button
            {
                Text = UiLocalization.Text("Common.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                DialogResult = DialogResult.Cancel,
                TabIndex = 2
            };
            UiTheme.ApplyDialogButton(cancelButton, UiGlyph.Cancel);
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(okButton);

            root.Controls.Add(label, 0, 0);
            root.Controls.Add(_prefixTextBox, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        public string Prefix
        {
            get { return _prefixTextBox.Text == null ? string.Empty : _prefixTextBox.Text.Trim(); }
        }

        public static bool TryEdit(IWin32Window owner, string currentPrefix, out string prefix)
        {
            using (var form = new CustomCommandPrefixEditForm(currentPrefix))
            {
                if (form.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(form.Prefix))
                {
                    prefix = form.Prefix;
                    return true;
                }
            }

            prefix = null;
            return false;
        }
    }
}
