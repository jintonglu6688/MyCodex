using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class AppUpdatePromptForm : Form
    {
        private readonly TextBox _releaseNotesTextBox;
        private readonly Button _updateButton;
        private readonly Button _laterButton;
        private readonly Button _ignoreButton;

        public AppUpdatePromptForm(
            string currentVersion,
            string latestVersion,
            string releaseNotes)
        {
            Text = UiLocalization.Text("AppUpdatePromptForm.Title", "MyCodex Update");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(640, 460);
            UiTheme.ApplyForm(this);

            var summaryLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Text = string.Format(
                    UiLocalization.Text(
                        "AppUpdatePromptForm.Summary.Template",
                        "A new MyCodex version is available.\r\n\r\nCurrent: {0}\r\nLatest: {1}"),
                    currentVersion,
                    latestVersion),
                TabStop = false
            };
            UiTheme.ApplyLabel(summaryLabel, true);

            var releaseNotesLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("AppUpdatePromptForm.ReleaseNotesLabel", "Release notes:"),
                TabStop = false
            };
            UiTheme.ApplyLabel(releaseNotesLabel, true);

            _releaseNotesTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("AppUpdatePromptForm.Accessible.ReleaseNotes", "Release notes"),
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                TabIndex = 0,
                Text = string.IsNullOrWhiteSpace(releaseNotes)
                    ? UiLocalization.Text("AppUpdatePromptForm.ReleaseNotesEmpty", "No release notes were provided.")
                    : releaseNotes.Trim()
            };
            UiTheme.ApplyTextBox(_releaseNotesTextBox, false, true);

            _updateButton = new Button
            {
                Text = UiLocalization.Text("AppUpdatePromptForm.Button.UpdateNow", "Update Now(&U)"),
                AccessibleName = UiLocalization.Text("AppUpdatePromptForm.Accessible.UpdateNow", "Update now"),
                DialogResult = DialogResult.OK,
                TabIndex = 1
            };
            UiTheme.ApplyDialogButton(_updateButton, UiGlyph.Download);

            _laterButton = new Button
            {
                Text = UiLocalization.Text("AppUpdatePromptForm.Button.Later", "Later(&L)"),
                AccessibleName = UiLocalization.Text("AppUpdatePromptForm.Accessible.Later", "Later"),
                DialogResult = DialogResult.Cancel,
                TabIndex = 2
            };
            UiTheme.ApplyDialogButton(_laterButton, UiGlyph.Cancel);

            _ignoreButton = new Button
            {
                Text = UiLocalization.Text("AppUpdatePromptForm.Button.Ignore", "Ignore(&I)"),
                AccessibleName = UiLocalization.Text("AppUpdatePromptForm.Accessible.Ignore", "Ignore update"),
                DialogResult = DialogResult.Ignore,
                TabIndex = 3
            };
            UiTheme.ApplyDialogButton(_ignoreButton, UiGlyph.Cancel);

            var buttonPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            buttonPanel.Controls.Add(_ignoreButton);
            buttonPanel.Controls.Add(_laterButton);
            buttonPanel.Controls.Add(_updateButton);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                RowCount = 4,
                ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(summaryLabel, 0, 0);
            layout.Controls.Add(releaseNotesLabel, 0, 1);
            layout.Controls.Add(_releaseNotesTextBox, 0, 2);
            layout.Controls.Add(buttonPanel, 0, 3);
            Controls.Add(layout);

            AcceptButton = _updateButton;
            CancelButton = _laterButton;
            Shown += AppUpdatePromptForm_Shown;
        }

        private void AppUpdatePromptForm_Shown(object sender, EventArgs e)
        {
            _releaseNotesTextBox.Focus();
            TextBoxCaretHelper.MoveToStart(_releaseNotesTextBox);
        }
    }
}
