using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class WslWorkspacePathForm : Form
    {
        private readonly bool _isRemoteMac;
        private readonly TextBox _pathTextBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public WslWorkspacePathForm(string initialPath)
            : this(initialPath, false)
        {
        }

        public WslWorkspacePathForm(string initialPath, bool isRemoteMac)
        {
            _isRemoteMac = isRemoteMac;
            Text = UiLocalization.Text(
                _isRemoteMac ? "WslWorkspacePathForm.Title.RemoteMac" : "WslWorkspacePathForm.Title",
                _isRemoteMac ? "Open Remote Unix Workspace" : "Open WSL Workspace");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 180);
            UiTheme.ApplyForm(this);

            _pathTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = string.IsNullOrWhiteSpace(initialPath) ? "/home" : initialPath.Trim(),
                AccessibleName = UiLocalization.Text(
                    _isRemoteMac ? "WslWorkspacePathForm.Accessible.Path.RemoteMac" : "WslWorkspacePathForm.Accessible.Path",
                    _isRemoteMac ? "Remote Unix workspace path" : "WSL workspace path")
            };
            UiTheme.ApplyTextBox(_pathTextBox, false, false);

            _okButton = new Button
            {
                Text = UiLocalization.Text("WslWorkspacePathForm.Button.Open", "Open(&O)"),
                DialogResult = DialogResult.None,
                Width = 96,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                UseMnemonic = true
            };
            _cancelButton = new Button
            {
                Text = UiLocalization.Text("WslWorkspacePathForm.Button.Cancel", "&Cancel"),
                DialogResult = DialogResult.Cancel,
                Width = 96,
                Height = 32,
                UseMnemonic = true
            };
            UiTheme.ApplyButton(_okButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _okButton.Click += OkButton_Click;
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            BuildLayout();
        }

        public string WorkspacePath { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _pathTextBox.SelectAll();
            _pathTextBox.Focus();
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            var path = _pathTextBox.Text;
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowValidationMessage(UiLocalization.Text(
                    _isRemoteMac ? "WslWorkspacePathForm.Message.PathRequired.RemoteMac" : "WslWorkspacePathForm.Message.PathRequired",
                    _isRemoteMac ? "Please enter a Remote Unix workspace path." : "Please enter a WSL workspace path."));
                return;
            }

            if (!WorkspacePathUtilities.LooksLikeUnixAbsolutePath(path))
            {
                ShowValidationMessage(UiLocalization.Text(
                    _isRemoteMac ? "WslWorkspacePathForm.Message.AbsolutePathRequired.RemoteMac" : "WslWorkspacePathForm.Message.AbsolutePathRequired",
                    _isRemoteMac
                        ? "Please enter an absolute Remote Unix path, such as /home/name/repo or /Users/name/repo."
                        : "Please enter an absolute WSL path, such as /home/user/repo."));
                return;
            }

            WorkspacePath = WorkspacePathUtilities.NormalizeUnixAbsolutePath(path);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowValidationMessage(string message)
        {
            MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _pathTextBox.Focus();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text(
                    _isRemoteMac ? "WslWorkspacePathForm.Label.Path.RemoteMac" : "WslWorkspacePathForm.Label.Path",
                    _isRemoteMac ? "Remote Unix workspace path" : "WSL workspace path"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var tip = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text(
                    _isRemoteMac ? "WslWorkspacePathForm.Tip.RemoteMac" : "WslWorkspacePathForm.Tip",
                    _isRemoteMac
                        ? "Example: /home/name/repo or /Users/name/repo. This path is resolved on the remote machine."
                        : "Example: /home/user/repo. Windows drive paths can still be opened with the normal workspace picker."),
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                Width = 200,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor
            };
            buttonPanel.Controls.Add(_okButton);
            buttonPanel.Controls.Add(_cancelButton);

            root.Controls.Add(label, 0, 0);
            root.Controls.Add(_pathTextBox, 0, 1);
            root.Controls.Add(tip, 0, 2);
            root.Controls.Add(buttonPanel, 0, 3);
            Controls.Add(root);
        }
    }
}
