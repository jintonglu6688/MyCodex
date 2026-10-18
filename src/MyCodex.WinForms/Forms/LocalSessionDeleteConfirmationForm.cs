using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class LocalSessionDeleteConfirmationForm : Form
    {
        private readonly CheckBox _deleteLocalHistoryCheckBox;
        private readonly Button _deleteButton;
        private readonly Button _cancelButton;

        internal LocalSessionDeleteConfirmationForm(string message, bool canDeleteLocalHistory)
            : this(message, canDeleteLocalHistory, false)
        {
        }

        internal LocalSessionDeleteConfirmationForm(string message, bool canDeleteLocalHistory, bool defaultDeleteLocalHistory)
        {
            Text = UiLocalization.Text("LocalSessionDeleteConfirmationForm.Title", "Delete");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(520, 260);
            Size = new Size(620, 320);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var messageLabel = new Label
            {
                Text = message ?? string.Empty,
                AccessibleName = UiLocalization.Text("LocalSessionDeleteConfirmationForm.Accessible.Message", "Delete confirmation"),
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.TopLeft,
                TabStop = false,
                Margin = new Padding(0, 0, 0, 10)
            };
            UiTheme.ApplyLabel(messageLabel, false);

            _deleteLocalHistoryCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("LocalSessionDeleteConfirmationForm.Check.DeleteLocalHistory", "Also delete local history records(&H)"),
                AccessibleName = UiLocalization.Text("LocalSessionDeleteConfirmationForm.Accessible.DeleteLocalHistory", "Also delete local history records"),
                AutoSize = true,
                Dock = DockStyle.Top,
                Checked = canDeleteLocalHistory && defaultDeleteLocalHistory,
                Enabled = canDeleteLocalHistory,
                TabIndex = 0,
                Margin = new Padding(0, 0, 0, 12)
            };
            UiTheme.ApplyCheckBox(_deleteLocalHistoryCheckBox);

            _deleteButton = new Button
            {
                Text = UiLocalization.Text("LocalSessionDeleteConfirmationForm.Button.Delete", "Delete(&D)"),
                AccessibleName = UiLocalization.Text("LocalSessionDeleteConfirmationForm.Accessible.Delete", "Delete"),
                AutoSize = true,
                TabIndex = 1
            };
            UiTheme.ApplyDialogButton(_deleteButton, UiGlyph.Ok);
            _deleteButton.Click += DeleteButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("Common.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = 2,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            var buttonPanel = UiControlFactory.CreateDialogButtonBar();
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_deleteButton);

            root.Controls.Add(messageLabel, 0, 0);
            root.Controls.Add(_deleteLocalHistoryCheckBox, 0, 1);
            root.Controls.Add(buttonPanel, 0, 2);
            Controls.Add(root);

            AcceptButton = _deleteButton;
            CancelButton = _cancelButton;
        }

        internal bool DeleteLocalHistory { get; private set; }

        internal bool DeleteLocalHistoryOptionEnabled
        {
            get { return _deleteLocalHistoryCheckBox.Enabled; }
        }

        internal bool DeleteLocalHistoryOptionChecked
        {
            get { return _deleteLocalHistoryCheckBox.Checked; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_deleteLocalHistoryCheckBox.Enabled)
            {
                _deleteLocalHistoryCheckBox.Focus();
            }
            else
            {
                _deleteButton.Focus();
            }
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            DeleteLocalHistory = _deleteLocalHistoryCheckBox.Enabled && _deleteLocalHistoryCheckBox.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
