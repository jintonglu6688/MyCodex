using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class ForkSessionForm : Form
    {
        private readonly TextBox _nameTextBox;
        private readonly ComboBox _createAfterComboBox;
        private readonly Button _createButton;
        private readonly Button _cancelButton;

        public ForkSessionForm(string sourceTitle, int forkIndex, string tipText = null)
        {
            Text = UiLocalization.Text("ForkSessionForm.Title", "Fork Current Session");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 240);
            UiTheme.ApplyForm(this);

            var branchLabel = string.Format(
                UiLocalization.Text("ForkSessionForm.BranchLabel", "Fork {0}"),
                Math.Max(1, forkIndex));

            var titleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                Text = UiLocalization.Text("ForkSessionForm.Heading", "Create an independent branch from current session"),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var tipLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Text = string.IsNullOrWhiteSpace(tipText)
                    ? string.Format(
                        UiLocalization.Text(
                            "ForkSessionForm.Tip.Template",
                            "Will create {0}. After creation, main and fork can continue independently with separate history, drafts, and attachments."),
                        branchLabel)
                    : tipText,
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
                RowCount = 2,
                TabStop = false
            };
            formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
            formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var nameLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("ForkSessionForm.Label.Name", "Name"),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            _nameTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("ForkSessionForm.Accessible.Name", "Name"),
                Text = SessionTitleStrategy.BuildForkTitle(sourceTitle, forkIndex)
            };
            UiTheme.ApplyTextBox(_nameTextBox, false, false);

            var createAfterLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("ForkSessionForm.Label.AfterCreate", "After Create"),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            _createAfterComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("ForkSessionForm.Accessible.AfterCreate", "After Create")
            };
            UiTheme.ApplyComboBox(_createAfterComboBox);
            _createAfterComboBox.Items.Add(UiLocalization.Text("ForkSessionForm.AfterCreate.StayOnMainRecommended", "Stay on main session (Recommended)"));
            _createAfterComboBox.Items.Add(UiLocalization.Text("ForkSessionForm.AfterCreate.SwitchToFork", "Switch to new fork"));
            _createAfterComboBox.SelectedIndex = 0;

            formTable.Controls.Add(nameLabel, 0, 0);
            formTable.Controls.Add(_nameTextBox, 1, 0);
            formTable.Controls.Add(createAfterLabel, 0, 1);
            formTable.Controls.Add(_createAfterComboBox, 1, 1);

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

            _createButton = new Button
            {
                Text = UiLocalization.Text("ForkSessionForm.Button.Create", "Create(&O)"),
                AccessibleName = UiLocalization.Text("ForkSessionForm.Accessible.Create", "Create"),
                Width = 96,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                UseMnemonic = true
            };
            UiTheme.ApplyButton(_createButton, UiGlyph.NewSession);
            _createButton.Click += CreateButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("ForkSessionForm.Button.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("ForkSessionForm.Accessible.Cancel", "Cancel"),
                Width = 96,
                Height = 32,
                UseMnemonic = true
            };
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);
            _cancelButton.Click += CancelButton_Click;

            buttonPanel.Controls.Add(_createButton);
            buttonPanel.Controls.Add(_cancelButton);
            footerPanel.Controls.Add(buttonPanel);

            contentPanel.Controls.Add(footerPanel);
            contentPanel.Controls.Add(formTable);
            contentPanel.Controls.Add(tipLabel);
            contentPanel.Controls.Add(titleLabel);

            Controls.Add(contentPanel);

            AcceptButton = _createButton;
            CancelButton = _cancelButton;
            Shown += ForkSessionForm_Shown;
        }

        public string ForkTitle
        {
            get { return _nameTextBox.Text.Trim(); }
        }

        public bool SwitchToForkAfterCreate
        {
            get { return _createAfterComboBox.SelectedIndex == 1; }
        }

        private void ForkSessionForm_Shown(object sender, EventArgs e)
        {
            _nameTextBox.Focus();
            _nameTextBox.SelectAll();
        }

        private void CreateButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ForkTitle))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("ForkSessionForm.Message.EnterForkName", "Please enter a fork name."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _nameTextBox.Focus();
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
