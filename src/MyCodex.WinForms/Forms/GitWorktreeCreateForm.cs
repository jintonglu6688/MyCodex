using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MyCodex.WinForms.Dialogs;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitWorktreeCreateForm : Form
    {
        private readonly TextBox _targetPathTextBox;
        private readonly TextBox _branchNameTextBox;
        private readonly TextBox _startPointTextBox;
        private readonly Button _browseButton;
        private readonly Button _createButton;
        private readonly Button _cancelButton;

        public GitWorktreeCreateForm(string workspacePath)
        {
            Text = UiLocalization.Text("GitWorktreeCreateForm.Title", "Create Worktree");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 230);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowOnly;
            UiTheme.ApplyForm(this);

            _targetPathTextBox = CreateTextBox(UiLocalization.Text("GitWorktreeCreateForm.Accessible.TargetPath", "Target path"), 0);
            _targetPathTextBox.Text = BuildDefaultTargetPath(workspacePath);
            _branchNameTextBox = CreateTextBox(UiLocalization.Text("GitWorktreeCreateForm.Accessible.BranchName", "Branch name"), 2);
            _startPointTextBox = CreateTextBox(UiLocalization.Text("GitWorktreeCreateForm.Accessible.StartPoint", "Start point"), 3);
            _startPointTextBox.Text = "HEAD";
            _browseButton = CreateButton(
                UiLocalization.Text("GitWorktreeCreateForm.Button.Browse", "Browse(&B)"),
                UiLocalization.Text("GitWorktreeCreateForm.Accessible.Browse", "Browse"),
                1,
                DialogResult.None);
            _createButton = CreateButton(
                UiLocalization.Text("GitWorktreeCreateForm.Button.Create", "Create(&C)"),
                UiLocalization.Text("GitWorktreeCreateForm.Accessible.Create", "Create"),
                4,
                DialogResult.OK);
            _cancelButton = CreateButton(
                UiLocalization.Text("GitWorktreeCreateForm.Button.Cancel", "Cancel(&A)"),
                UiLocalization.Text("GitWorktreeCreateForm.Accessible.Cancel", "Cancel"),
                5,
                DialogResult.Cancel);
            UiTheme.ApplyButton(_browseButton, UiGlyph.Info);
            UiTheme.ApplyButton(_createButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _browseButton.Click += (sender, e) => BrowseTargetPath();
            _createButton.Click += (sender, e) =>
            {
                TargetPath = (_targetPathTextBox.Text ?? string.Empty).Trim();
                BranchName = (_branchNameTextBox.Text ?? string.Empty).Trim();
                StartPoint = (_startPointTextBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(TargetPath) || string.IsNullOrWhiteSpace(BranchName))
                {
                    DialogResult = DialogResult.None;
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("GitWorktreeCreateForm.Message.Required", "Target path and branch name are required."),
                        Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (IsRootPath(TargetPath))
                {
                    DialogResult = DialogResult.None;
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("GitWorktreeCreateForm.Message.TargetPathRoot", "Target path cannot be a drive root. Choose a new folder under the drive."),
                        Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            };

            AcceptButton = _createButton;
            CancelButton = _cancelButton;
            BuildLayout();
        }

        public string TargetPath { get; private set; }

        public string BranchName { get; private set; }

        public string StartPoint { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _targetPathTextBox.Focus();
        }

        private void BrowseTargetPath()
        {
            string selectedPath;
            if (WindowsFolderPicker.TryPickFolder(
                this,
                UiLocalization.Text("GitWorktreeCreateForm.Dialog.TargetPath", "Select or create the target folder for the worktree."),
                Directory.Exists(_targetPathTextBox.Text) ? _targetPathTextBox.Text : null,
                out selectedPath))
            {
                _targetPathTextBox.Text = selectedPath;
                _branchNameTextBox.Focus();
            }
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var targetPanel = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            targetPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            targetPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            targetPanel.Controls.Add(CreateLabel(UiLocalization.Text("GitWorktreeCreateForm.Label.TargetPath", "Target path(&T)")), 0, 0);
            targetPanel.Controls.Add(_targetPathTextBox, 0, 1);
            targetPanel.Controls.Add(_browseButton, 1, 1);

            root.Controls.Add(targetPanel, 0, 0);
            root.Controls.Add(CreateLabeledTextBox(UiLocalization.Text("GitWorktreeCreateForm.Label.BranchName", "New branch name(&N)"), _branchNameTextBox), 0, 1);
            root.Controls.Add(CreateLabeledTextBox(UiLocalization.Text("GitWorktreeCreateForm.Label.StartPoint", "Start point(&S)"), _startPointTextBox), 0, 2);

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
            root.Controls.Add(footer, 0, 3);
            Controls.Add(root);
        }

        private static Control CreateLabeledTextBox(string labelText, TextBox textBox)
        {
            var panel = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(CreateLabel(labelText), 0, 0);
            panel.Controls.Add(textBox, 0, 1);
            return panel;
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

        private static TextBox CreateTextBox(string accessibleName, int tabIndex)
        {
            var textBox = new TextBox
            {
                AccessibleName = accessibleName,
                Dock = DockStyle.Fill,
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, false, false);
            return textBox;
        }

        private static string BuildDefaultTargetPath(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return string.Empty;
            }

            try
            {
                var fullPath = Path.GetFullPath(workspacePath.Trim());
                var workspaceName = new DirectoryInfo(fullPath).Name;
                var parent = Directory.GetParent(fullPath);
                if (string.IsNullOrWhiteSpace(workspaceName) || parent == null)
                {
                    return string.Empty;
                }

                var basePath = Path.Combine(parent.FullName, workspaceName + "-Worktree");
                if (!Directory.Exists(basePath) && !File.Exists(basePath))
                {
                    return basePath;
                }

                for (var i = 2; i < 100; i++)
                {
                    var candidate = basePath + "-" + i.ToString();
                    if (!Directory.Exists(candidate) && !File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                return basePath + "-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsRootPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                var trimmedPath = path.Trim();
                if (trimmedPath.Length == 2 && trimmedPath[1] == ':')
                {
                    return true;
                }

                if (trimmedPath == Path.DirectorySeparatorChar.ToString() ||
                    trimmedPath == Path.AltDirectorySeparatorChar.ToString())
                {
                    return true;
                }

                var fullPath = Path.GetFullPath(trimmedPath);
                var root = Path.GetPathRoot(fullPath);
                return !string.IsNullOrWhiteSpace(root) &&
                       string.Equals(
                           fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                           root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex, DialogResult dialogResult)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 104,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex,
                DialogResult = dialogResult
            };
        }
    }
}
