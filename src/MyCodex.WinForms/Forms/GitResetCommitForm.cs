using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitResetCommitForm : Form
    {
        private readonly ListBox _modeListBox;
        private readonly Button _resetButton;
        private readonly Button _cancelButton;
        private readonly string _commitText;

        public GitResetCommitForm(string commitText)
        {
            _commitText = commitText ?? string.Empty;
            SelectedMode = GitResetMode.Mixed;

            Text = UiLocalization.Text("GitResetCommitForm.Title", "Roll Back");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(740, 300);
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            var summaryLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                Text = string.Format(
                    UiLocalization.Text(
                        "GitResetCommitForm.Summary.Template",
                        "Roll back the current branch to:\r\n{0}\r\nCommits after this point are removed from the current branch history. This operation uses git reset."),
                    _commitText),
                TabStop = false
            };
            UiTheme.ApplyLabel(summaryLabel, true);

            _modeListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                HorizontalScrollbar = true,
                AccessibleName = UiLocalization.Text("GitResetCommitForm.Accessible.Mode", "Mode"),
                TabIndex = 0
            };
            UiTheme.ApplyListBox(_modeListBox);
            _modeListBox.Items.Add(new ResetModeItem(
                GitResetMode.Soft,
                UiLocalization.Text(
                    "GitResetCommitForm.Mode.Soft",
                    "Soft - move the branch pointer only; keep later commit changes staged.")));
            _modeListBox.Items.Add(new ResetModeItem(
                GitResetMode.Mixed,
                UiLocalization.Text(
                    "GitResetCommitForm.Mode.Mixed",
                    "Mixed - move the branch pointer; keep later commit changes unstaged.")));
            _modeListBox.Items.Add(new ResetModeItem(
                GitResetMode.Hard,
                UiLocalization.Text(
                    "GitResetCommitForm.Mode.Hard",
                    "Hard - move the branch pointer and discard later commit changes from the working tree.")));
            _modeListBox.SelectedIndex = 1;
            _modeListBox.SelectedIndexChanged += (sender, e) => UpdateSelectedMode();
            _modeListBox.DoubleClick += (sender, e) => ConfirmAndClose();
            _modeListBox.KeyDown += ModeListBox_KeyDown;

            _resetButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitResetCommitForm.Button.Reset", "Roll Back(&R)"),
                UiLocalization.Text("GitResetCommitForm.Accessible.Reset", "Roll back"),
                1,
                118);
            _cancelButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitResetCommitForm.Button.Cancel", "&Cancel"),
                UiLocalization.Text("GitResetCommitForm.Accessible.Cancel", "Cancel"),
                2,
                118);
            UiTheme.ApplyButton(_resetButton, UiGlyph.Error);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _resetButton.Click += (sender, e) => ConfirmAndClose();
            _cancelButton.Click += (sender, e) => DialogResult = DialogResult.Cancel;

            BuildLayout(summaryLabel);
            AcceptButton = _resetButton;
            CancelButton = _cancelButton;
            Shown += (sender, e) => _modeListBox.Focus();
        }

        public GitResetMode SelectedMode { get; private set; }

        private void ModeListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            ConfirmAndClose();
        }

        private void UpdateSelectedMode()
        {
            var item = _modeListBox.SelectedItem as ResetModeItem;
            if (item != null)
            {
                SelectedMode = item.Mode;
            }
        }

        private void ConfirmAndClose()
        {
            UpdateSelectedMode();
            if (SelectedMode == GitResetMode.Hard)
            {
                var result = MessageBox.Show(
                    this,
                    string.Format(
                        UiLocalization.Text(
                            "GitResetCommitForm.Confirm.Hard.Template",
                            "Hard rollback will discard working tree content created by later commits.\r\n\r\n{0}\r\n\r\nContinue?"),
                        _commitText),
                    AppConstants.AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (result != DialogResult.Yes)
                {
                    _modeListBox.Focus();
                    return;
                }
            }

            DialogResult = DialogResult.OK;
        }

        private void BuildLayout(Control summaryLabel)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

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
            footer.Controls.Add(_resetButton);

            root.Controls.Add(summaryLabel, 0, 0);
            root.Controls.Add(_modeListBox, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
        }

        private sealed class ResetModeItem
        {
            public ResetModeItem(GitResetMode mode, string text)
            {
                Mode = mode;
                Text = text ?? string.Empty;
            }

            public GitResetMode Mode { get; }

            private string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
