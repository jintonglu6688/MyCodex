using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitStashBranchCreateForm : Form
    {
        private readonly TextBox _branchNameTextBox;
        private readonly Button _createButton;
        private readonly Button _cancelButton;

        public GitStashBranchCreateForm()
        {
            Text = UiLocalization.Text("GitStashBranchCreateForm.Title", "Create Branch from Stash");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 132);
            UiTheme.ApplyForm(this);

            _branchNameTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("GitStashBranchCreateForm.Accessible.BranchName", "Branch name"),
                Dock = DockStyle.Fill,
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_branchNameTextBox, false, false);

            _createButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitStashBranchCreateForm.Button.Create", "Create(&C)"),
                UiLocalization.Text("GitStashBranchCreateForm.Accessible.Create", "Create"),
                1,
                104,
                DialogResult.OK);
            _cancelButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitStashBranchCreateForm.Button.Cancel", "Cancel(&A)"),
                UiLocalization.Text("GitStashBranchCreateForm.Accessible.Cancel", "Cancel"),
                2,
                104,
                DialogResult.Cancel);
            UiTheme.ApplyButton(_createButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _createButton.Click += (sender, e) =>
            {
                BranchName = (_branchNameTextBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(BranchName))
                {
                    DialogResult = DialogResult.None;
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("GitStashBranchCreateForm.Message.BranchNameRequired", "Branch name cannot be empty."),
                        Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            };

            AcceptButton = _createButton;
            CancelButton = _cancelButton;
            BuildLayout();
        }

        public string BranchName { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _branchNameTextBox.Focus();
        }

        private void BuildLayout()
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
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = new Label
            {
                Text = UiLocalization.Text("GitStashBranchCreateForm.Label.BranchName", "Branch name(&B)"),
                AutoSize = true,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            label.Focus();

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_cancelButton);
            footer.Controls.Add(_createButton);

            root.Controls.Add(label, 0, 0);
            root.Controls.Add(_branchNameTextBox, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
        }
    }
}
