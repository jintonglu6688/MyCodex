using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitCommitMessageEditForm : Form
    {
        private readonly TextBox _subjectTextBox;
        private readonly TextBox _bodyTextBox;
        private readonly Label _warningLabel;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public GitCommitMessageEditForm(string subject, string body, bool mayRequireForcePush)
        {
            Text = UiLocalization.Text("GitCommitMessageEditForm.Title", "Edit Commit Message");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(620, 420);
            Size = new Size(720, 500);
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);

            _subjectTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("GitCommitMessageEditForm.Accessible.Subject", "Subject"),
                Dock = DockStyle.Fill,
                Text = subject ?? string.Empty,
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_subjectTextBox, false, false);

            _bodyTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("GitCommitMessageEditForm.Accessible.Body", "Details"),
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                Text = body ?? string.Empty,
                TabIndex = 1
            };
            UiTheme.ApplyTextBox(_bodyTextBox, false, false);

            _warningLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = mayRequireForcePush
                    ? UiLocalization.Text("GitCommitMessageEditForm.Warning.Published", "This commit may already be pushed. After amending, you may need force-with-lease.")
                    : UiLocalization.Text("GitCommitMessageEditForm.Warning.Local", "This changes the latest local commit message."),
                TabStop = false
            };
            UiTheme.ApplyLabel(_warningLabel, true);

            _okButton = CreateButton(UiLocalization.Text("GitCommitMessageEditForm.Button.OK", "Amend(&A)"), UiLocalization.Text("GitCommitMessageEditForm.Accessible.OK", "Amend"), 2);
            _cancelButton = CreateButton(UiLocalization.Text("GitCommitMessageEditForm.Button.Cancel", "Cancel(&C)"), UiLocalization.Text("GitCommitMessageEditForm.Accessible.Cancel", "Cancel"), 3);
            UiTheme.ApplyButton(_okButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);
            _okButton.Click += (sender, e) => Accept();
            _cancelButton.Click += (sender, e) => DialogResult = DialogResult.Cancel;

            BuildLayout();
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }

        public string CommitSubject
        {
            get { return (_subjectTextBox.Text ?? string.Empty).Trim(); }
        }

        public string CommitBody
        {
            get { return (_bodyTextBox.Text ?? string.Empty).Trim(); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _subjectTextBox.Focus();
            _subjectTextBox.SelectAll();
        }

        private void Accept()
        {
            if (string.IsNullOrWhiteSpace(CommitSubject))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitCommitMessageEditForm.Message.SubjectRequired", "Enter a commit subject first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _subjectTextBox.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var subjectLabel = CreateLabel(UiLocalization.Text("GitCommitMessageEditForm.Label.Subject", "Subject"));
            var bodyLabel = CreateLabel(UiLocalization.Text("GitCommitMessageEditForm.Label.Body", "Details"));
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_okButton);
            footer.Controls.Add(_cancelButton);

            root.Controls.Add(subjectLabel, 0, 0);
            root.Controls.Add(_subjectTextBox, 0, 1);
            root.Controls.Add(bodyLabel, 0, 2);
            root.Controls.Add(_bodyTextBox, 0, 3);
            root.Controls.Add(_warningLabel, 0, 4);
            root.Controls.Add(footer, 0, 5);
            Controls.Add(root);
        }

        private static Label CreateLabel(string text)
        {
            var label = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.MutedTextColor,
                Text = text ?? string.Empty,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 118,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }
    }
}
