using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitHubCommentForm : Form
    {
        private readonly TextBox _bodyTextBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public GitHubCommentForm()
        {
            Text = UiLocalization.Text("GitHubCommentForm.Title", "Add Comment");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(520, 340);
            Size = new Size(620, 420);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _bodyTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitHubCommentForm.Accessible.Body", "Comment"),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_bodyTextBox, false, true);

            _okButton = CreateButton(
                UiLocalization.Text("GitHubCommentForm.Button.OK", "&OK"),
                UiLocalization.Text("GitHubCommentForm.Accessible.OK", "OK"),
                DialogResult.OK,
                1);
            _cancelButton = CreateButton(
                UiLocalization.Text("GitHubCommentForm.Button.Cancel", "&Cancel"),
                UiLocalization.Text("GitHubCommentForm.Accessible.Cancel", "Cancel"),
                DialogResult.Cancel,
                2);

            _okButton.Click += (sender, e) =>
            {
                if (string.IsNullOrWhiteSpace(Body))
                {
                    DialogResult = DialogResult.None;
                    MessageBox.Show(this, UiLocalization.Text("GitHubCommentForm.Message.BodyRequired", "Comment cannot be empty."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            BuildLayout();
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }

        public string Body
        {
            get { return _bodyTextBox.Text == null ? string.Empty : _bodyTextBox.Text.Trim(); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _bodyTextBox.Focus();
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = UiControlFactory.CreateMutedLabel(UiLocalization.Text("GitHubCommentForm.Label.Body", "Comment(&C)"));
            var footer = UiControlFactory.CreateDialogButtonBar(UiTheme.WindowBackColor);
            footer.Controls.Add(_okButton);
            footer.Controls.Add(_cancelButton);

            root.Controls.Add(label, 0, 0);
            root.Controls.Add(_bodyTextBox, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
        }

        private static Button CreateButton(string text, string accessibleName, DialogResult dialogResult, int tabIndex)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                DialogResult = dialogResult,
                Width = 112,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
            UiTheme.ApplyButton(button, dialogResult == DialogResult.OK ? UiGlyph.Ok : UiGlyph.Cancel);
            return button;
        }
    }
}
