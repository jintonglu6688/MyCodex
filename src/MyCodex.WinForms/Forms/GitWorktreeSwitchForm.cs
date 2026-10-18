using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitWorktreeSwitchForm : Form
    {
        private readonly ComboBox _modeComboBox;
        private readonly TextBox _targetTextBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public GitWorktreeSwitchForm(string targetPath, string branchText)
        {
            Text = UiLocalization.Text("GitWorktreeSwitchForm.Title", "Switch Worktree");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 230);
            UiTheme.ApplyForm(this);

            _modeComboBox = new ComboBox
            {
                AccessibleName = UiLocalization.Text("GitWorktreeSwitchForm.Accessible.Mode", "Mode"),
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                TabIndex = 0
            };
            UiTheme.ApplyComboBox(_modeComboBox);
            _modeComboBox.Items.Add(new ModeOption(
                UiLocalization.Text("GitWorktreeSwitchForm.Mode.SwitchOnly", "Switch only, restore existing session"),
                GitWorktreeSwitchMode.SwitchOnly));
            _modeComboBox.Items.Add(new ModeOption(
                UiLocalization.Text("GitWorktreeSwitchForm.Mode.NewSession", "Switch and create new session"),
                GitWorktreeSwitchMode.NewSession));
            _modeComboBox.SelectedIndex = 0;

            _targetTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("GitWorktreeSwitchForm.Accessible.Target", "Target"),
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                TabIndex = 1,
                Text = string.Format(
                    UiLocalization.Text("GitWorktreeSwitchForm.Target.Template", "Path: {0}\r\nBranch: {1}"),
                    targetPath ?? string.Empty,
                    branchText ?? string.Empty)
            };
            UiTheme.ApplyTextBox(_targetTextBox, true, true);

            _okButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitWorktreeSwitchForm.Button.OK", "OK(&O)"),
                UiLocalization.Text("GitWorktreeSwitchForm.Accessible.OK", "OK"),
                2,
                104,
                DialogResult.OK);
            _cancelButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitWorktreeSwitchForm.Button.Cancel", "Cancel(&C)"),
                UiLocalization.Text("GitWorktreeSwitchForm.Accessible.Cancel", "Cancel"),
                3,
                104,
                DialogResult.Cancel);
            UiTheme.ApplyButton(_okButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _okButton.Click += (sender, e) =>
            {
                var option = _modeComboBox.SelectedItem as ModeOption;
                if (option == null)
                {
                    DialogResult = DialogResult.None;
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("GitWorktreeSwitchForm.Message.SelectMode", "Select a switch mode."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Mode = option.Mode;
            };

            BuildLayout();
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }

        public GitWorktreeSwitchMode Mode { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _modeComboBox.Focus();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(CreateLabel(UiLocalization.Text("GitWorktreeSwitchForm.Label.Mode", "Mode(&M)")), 0, 0);
            root.Controls.Add(_modeComboBox, 0, 1);
            root.Controls.Add(_targetTextBox, 0, 2);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_cancelButton);
            footer.Controls.Add(_okButton);
            root.Controls.Add(footer, 0, 3);
            Controls.Add(root);
        }

        private static Label CreateLabel(string text)
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

        private sealed class ModeOption
        {
            public ModeOption(string text, GitWorktreeSwitchMode mode)
            {
                Text = text ?? string.Empty;
                Mode = mode;
            }

            public string Text { get; private set; }

            public GitWorktreeSwitchMode Mode { get; private set; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
