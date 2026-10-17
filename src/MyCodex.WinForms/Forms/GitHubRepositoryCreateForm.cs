using System.Drawing;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitHubRepositoryCreateForm : Form
    {
        private readonly TextBox _nameTextBox;
        private readonly TextBox _descriptionTextBox;
        private readonly CheckBox _privateCheckBox;
        private readonly CheckBox _addReadmeCheckBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public GitHubRepositoryCreateForm(string title, string initialName, bool allowReadme)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(620, 250);
            UiTheme.ApplyForm(this);

            _nameTextBox = CreateTextBox(UiLocalization.Text("GitHubRepositoryCreateForm.Accessible.Name", "Repository name"), 0);
            _nameTextBox.Text = initialName ?? string.Empty;
            _descriptionTextBox = CreateTextBox(UiLocalization.Text("GitHubRepositoryCreateForm.Accessible.Description", "Description"), 1);
            _privateCheckBox = new CheckBox
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitHubRepositoryCreateForm.Check.Private", "Private(&P)"),
                AccessibleName = UiLocalization.Text("GitHubRepositoryCreateForm.Accessible.Private", "Private"),
                Checked = true,
                UseMnemonic = true,
                TabIndex = 2
            };
            UiTheme.ApplyCheckBox(_privateCheckBox);

            _addReadmeCheckBox = new CheckBox
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitHubRepositoryCreateForm.Check.AddReadme", "Add README(&R)"),
                AccessibleName = UiLocalization.Text("GitHubRepositoryCreateForm.Accessible.AddReadme", "Add README"),
                Enabled = allowReadme,
                UseMnemonic = true,
                TabIndex = 3
            };
            UiTheme.ApplyCheckBox(_addReadmeCheckBox);

            _okButton = CreateButton(
                UiLocalization.Text("GitHubRepositoryCreateForm.Button.Create", "Create(&O)"),
                UiLocalization.Text("GitHubRepositoryCreateForm.Accessible.Create", "Create"),
                4);
            _cancelButton = CreateButton(
                UiLocalization.Text("GitHubRepositoryCreateForm.Button.Cancel", "&Cancel"),
                UiLocalization.Text("GitHubRepositoryCreateForm.Accessible.Cancel", "Cancel"),
                5);
            UiTheme.ApplyButton(_okButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _okButton.Click += (sender, e) => AcceptValue();
            _cancelButton.Click += (sender, e) => DialogResult = DialogResult.Cancel;

            BuildLayout(allowReadme);
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            Shown += (sender, e) =>
            {
                _nameTextBox.SelectAll();
                _nameTextBox.Focus();
            };
        }

        public GitHubCreateRepositoryRequest Request
        {
            get
            {
                return new GitHubCreateRepositoryRequest
                {
                    Name = (_nameTextBox.Text ?? string.Empty).Trim(),
                    Description = (_descriptionTextBox.Text ?? string.Empty).Trim(),
                    IsPrivate = _privateCheckBox.Checked,
                    AddReadme = _addReadmeCheckBox.Enabled && _addReadmeCheckBox.Checked
                };
            }
        }

        private void AcceptValue()
        {
            if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitHubRepositoryCreateForm.Message.NameRequired", "Enter a repository name first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _nameTextBox.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
        }

        private void BuildLayout(bool allowReadme)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, allowReadme ? 34F : 0F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            root.Controls.Add(CreateLabel(UiLocalization.Text("GitHubRepositoryCreateForm.Label.Name", "Repository name")), 0, 0);
            root.Controls.Add(_nameTextBox, 0, 1);
            root.Controls.Add(CreateLabel(UiLocalization.Text("GitHubRepositoryCreateForm.Label.Description", "Description")), 0, 2);
            root.Controls.Add(_descriptionTextBox, 0, 3);
            root.Controls.Add(_privateCheckBox, 0, 4);
            if (allowReadme)
            {
                root.Controls.Add(_addReadmeCheckBox, 0, 5);
            }

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_cancelButton);
            footer.Controls.Add(_okButton);
            root.Controls.Add(footer, 0, 6);
            Controls.Add(root);
        }

        private static Label CreateLabel(string text)
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

        private static TextBox CreateTextBox(string accessibleName, int tabIndex)
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

        private static Button CreateButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 112,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }
    }
}
