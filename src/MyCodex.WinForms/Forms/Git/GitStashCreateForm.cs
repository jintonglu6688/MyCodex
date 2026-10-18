using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitStashCreateForm : Form
    {
        private readonly TextBox _messageTextBox;
        private readonly CheckBox _includeUntrackedCheckBox;
        private readonly Button _saveButton;
        private readonly Button _cancelButton;

        public GitStashCreateForm()
        {
            Text = UiLocalization.Text("GitStashCreateForm.Title", "Save Stash");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(620, 210);
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _messageTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitStashCreateForm.Accessible.Message", "Message"),
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_messageTextBox, false, false);

            _includeUntrackedCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("GitStashCreateForm.Check.IncludeUntracked", "Include untracked files(&U)"),
                AccessibleName = UiLocalization.Text("GitStashCreateForm.Accessible.IncludeUntracked", "Include untracked files"),
                AutoSize = true,
                TabIndex = 1
            };
            UiTheme.ApplyCheckBox(_includeUntrackedCheckBox);

            _saveButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitStashCreateForm.Button.Save", "Save(&S)"),
                UiLocalization.Text("GitStashCreateForm.Accessible.Save", "Save"),
                2,
                118);
            _cancelButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitStashCreateForm.Button.Cancel", "&Cancel"),
                UiLocalization.Text("GitStashCreateForm.Accessible.Cancel", "Cancel"),
                3,
                118);
            UiTheme.ApplyButton(_saveButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _saveButton.Click += (sender, e) =>
            {
                StashMessage = (_messageTextBox.Text ?? string.Empty).Trim();
                IncludeUntracked = _includeUntrackedCheckBox.Checked;
                DialogResult = DialogResult.OK;
            };
            _cancelButton.Click += (sender, e) => DialogResult = DialogResult.Cancel;

            BuildLayout();
            AcceptButton = _saveButton;
            CancelButton = _cancelButton;
            Shown += (sender, e) => _messageTextBox.Focus();
        }

        public string StashMessage { get; private set; }

        public bool IncludeUntracked { get; private set; }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var messageLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitStashCreateForm.Label.Message", "Message"),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(messageLabel, true);

            var footer = UiControlFactory.CreateDialogButtonBar(UiTheme.WindowBackColor);
            footer.Controls.Add(_cancelButton);
            footer.Controls.Add(_saveButton);

            root.Controls.Add(messageLabel, 0, 0);
            root.Controls.Add(_messageTextBox, 0, 1);
            root.Controls.Add(_includeUntrackedCheckBox, 0, 2);
            root.Controls.Add(footer, 0, 3);
            Controls.Add(root);
        }
    }
}
