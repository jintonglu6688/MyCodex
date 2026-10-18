using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitBranchCreateForm : Form
    {
        private readonly TextBox _branchNameTextBox;
        private readonly Button _createButton;
        private readonly Button _cancelButton;

        public GitBranchCreateForm()
            : this(
                UiLocalization.Text("GitBranchCreateForm.Title", "New Branch"),
                UiLocalization.Text("GitBranchCreateForm.Label.Name", "Branch name"),
                UiLocalization.Text("GitBranchCreateForm.Button.Create", "Create(&O)"),
                UiLocalization.Text("GitBranchCreateForm.Accessible.Create", "Create"),
                null)
        {
        }

        public GitBranchCreateForm(string titleText, string labelText, string acceptButtonText, string acceptAccessibleName, string initialBranchName)
        {
            Text = titleText ?? UiLocalization.Text("GitBranchCreateForm.Title", "New Branch");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 150);
            UiTheme.ApplyForm(this);

            _branchNameTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitBranchCreateForm.Accessible.Name", "Name"),
                Text = initialBranchName ?? string.Empty,
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_branchNameTextBox, false, false);

            _createButton = UiControlFactory.CreateActionButton(
                acceptButtonText ?? UiLocalization.Text("GitBranchCreateForm.Button.Create", "Create(&O)"),
                acceptAccessibleName ?? UiLocalization.Text("GitBranchCreateForm.Accessible.Create", "Create"),
                1,
                112);
            _cancelButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitBranchCreateForm.Button.Cancel", "&Cancel"),
                UiLocalization.Text("GitBranchCreateForm.Accessible.Cancel", "Cancel"),
                2,
                112);
            UiTheme.ApplyDialogButton(_createButton, UiGlyph.Ok);
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            _createButton.Click += (sender, e) => AcceptBranchName();
            _cancelButton.Click += (sender, e) => DialogResult = DialogResult.Cancel;

            BuildLayout(labelText);
            AcceptButton = _createButton;
            CancelButton = _cancelButton;
            Shown += (sender, e) =>
            {
                Activate();
                _branchNameTextBox.Focus();
                _branchNameTextBox.SelectAll();
            };
        }
        public string BranchName
        {
            get { return (_branchNameTextBox.Text ?? string.Empty).Trim(); }
        }

        private void AcceptBranchName()
        {
            if (string.IsNullOrWhiteSpace(BranchName))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitBranchCreateForm.Message.NameRequired", "Enter a branch name first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _branchNameTextBox.Focus();
                _branchNameTextBox.SelectAll();
                return;
            }

            DialogResult = DialogResult.OK;
        }

        private void BuildLayout(string labelText)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var nameLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = labelText ?? UiLocalization.Text("GitBranchCreateForm.Label.Name", "Branch name"),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(nameLabel, true);

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
            footer.Controls.Add(_createButton);

            root.Controls.Add(nameLabel, 0, 0);
            root.Controls.Add(_branchNameTextBox, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
        }
    }
}
