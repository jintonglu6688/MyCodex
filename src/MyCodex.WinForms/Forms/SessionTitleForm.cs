using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class SessionTitleForm : Form
    {
        private readonly TextBox _titleTextBox;
        private readonly Button _confirmButton;
        private readonly Button _cancelButton;
        private readonly string _emptyTitleMessage;

        public SessionTitleForm(
            string dialogTitle,
            string headingText,
            string tipText,
            string initialTitle,
            string confirmButtonText,
            string confirmAccessibleName,
            string emptyTitleMessage)
        {
            Text = string.IsNullOrWhiteSpace(dialogTitle)
                ? UiLocalization.Text("SessionTitleForm.Title.EditTitle", "Edit Title")
                : dialogTitle;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, 214);
            UiTheme.ApplyForm(this);

            _emptyTitleMessage = string.IsNullOrWhiteSpace(emptyTitleMessage)
                ? UiLocalization.Text("SessionTitleForm.Message.EnterTitle", "Please enter a title.")
                : emptyTitleMessage;

            var titleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                Text = headingText ?? UiLocalization.Text("SessionTitleForm.Heading.EditTitle", "Edit Title"),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var tipLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 38,
                Text = tipText ?? string.Empty,
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };

            var formTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                TabStop = false
            };
            formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
            formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var nameLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("SessionTitleForm.Label.Title", "Title"),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            _titleTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("SessionTitleForm.Accessible.Title", "Title"),
                Text = initialTitle ?? string.Empty
            };
            UiTheme.ApplyTextBox(_titleTextBox, false, false);

            formTable.Controls.Add(nameLabel, 0, 0);
            formTable.Controls.Add(_titleTextBox, 1, 0);

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(0, 12, 0, 0),
                TabStop = false
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 208,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            _confirmButton = new Button
            {
                Text = string.IsNullOrWhiteSpace(confirmButtonText)
                    ? UiLocalization.Text("SessionTitleForm.Button.Confirm", "Save(&S)")
                    : confirmButtonText,
                AccessibleName = string.IsNullOrWhiteSpace(confirmAccessibleName)
                    ? UiLocalization.Text("SessionTitleForm.Accessible.Confirm", "Save")
                    : confirmAccessibleName,
                Width = 96,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                UseMnemonic = true
            };
            UiTheme.ApplyButton(_confirmButton, UiGlyph.Ok);
            _confirmButton.Click += ConfirmButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("SessionTitleForm.Button.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("SessionTitleForm.Accessible.Cancel", "Cancel"),
                Width = 96,
                Height = 32,
                UseMnemonic = true
            };
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);
            _cancelButton.Click += CancelButton_Click;

            buttonPanel.Controls.Add(_confirmButton);
            buttonPanel.Controls.Add(_cancelButton);
            footerPanel.Controls.Add(buttonPanel);

            contentPanel.Controls.Add(footerPanel);
            contentPanel.Controls.Add(formTable);
            contentPanel.Controls.Add(tipLabel);
            contentPanel.Controls.Add(titleLabel);

            Controls.Add(contentPanel);

            AcceptButton = _confirmButton;
            CancelButton = _cancelButton;
            Shown += SessionTitleForm_Shown;
        }

        public string SessionTitle
        {
            get { return (_titleTextBox.Text ?? string.Empty).Trim(); }
        }

        private void SessionTitleForm_Shown(object sender, EventArgs e)
        {
            _titleTextBox.Focus();
            _titleTextBox.SelectAll();
        }

        private void ConfirmButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SessionTitle))
            {
                MessageBox.Show(this, _emptyTitleMessage, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _titleTextBox.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
