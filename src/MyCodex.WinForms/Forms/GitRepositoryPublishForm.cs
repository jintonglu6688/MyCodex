using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal enum GitRepositoryPublishAction
    {
        BindGitHubRepository,
        CreateGitHubRepository,
        BindManualRemote
    }

    internal sealed class GitRepositoryPublishForm : Form
    {
        private readonly string _preferredRepositoryName;
        private readonly TabControl _sourceTabControl;
        private readonly TabPage _githubTabPage;
        private readonly TabPage _createTabPage;
        private readonly TabPage _manualTabPage;
        private readonly ListView _githubRepositoryListView;
        private readonly TextBox _createNameTextBox;
        private readonly TextBox _createDescriptionTextBox;
        private readonly CheckBox _createPrivateCheckBox;
        private readonly TextBox _manualRemoteUrlTextBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly bool _allowGitHubCreate;

        public GitRepositoryPublishForm(IReadOnlyList<GitHubRepositoryEntry> githubRepositories, string preferredRepositoryName, bool allowGitHubCreate)
        {
            _preferredRepositoryName = preferredRepositoryName ?? string.Empty;
            _allowGitHubCreate = allowGitHubCreate;

            Text = UiLocalization.Text("GitRepositoryPublishForm.Title", "Publish Current Repository");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(800, 410);
            UiTheme.ApplyForm(this);

            _sourceTabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                AccessibleName = string.Empty,
                TabIndex = 0
            };
            _githubTabPage = new TabPage(UiLocalization.Text("GitRepositoryPublishForm.Tab.GitHubEmptyRepositories", "GitHub Empty Repositories"));
            _createTabPage = new TabPage(UiLocalization.Text("GitRepositoryPublishForm.Tab.CreateGitHubRepository", "Create GitHub Repository"));
            _manualTabPage = new TabPage(UiLocalization.Text("GitRepositoryPublishForm.Tab.Manual", "Manual"));
            _githubRepositoryListView = CreateGitHubRepositoryListView();
            _createNameTextBox = CreateTextBox(UiLocalization.Text("GitRepositoryPublishForm.Accessible.Name", "Repository name"), 2);
            _createNameTextBox.Text = _preferredRepositoryName;
            _createDescriptionTextBox = CreateTextBox(UiLocalization.Text("GitRepositoryPublishForm.Accessible.Description", "Description"), 3);
            _createPrivateCheckBox = new CheckBox
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitRepositoryPublishForm.Check.Private", "Private(&P)"),
                AccessibleName = UiLocalization.Text("GitRepositoryPublishForm.Accessible.Private", "Private"),
                Checked = true,
                UseMnemonic = true,
                TabIndex = 4
            };
            UiTheme.ApplyCheckBox(_createPrivateCheckBox);
            _manualRemoteUrlTextBox = CreateTextBox(UiLocalization.Text("GitRepositoryPublishForm.Accessible.RemoteUrl", "Remote URL"), 5);

            _okButton = CreateButton(
                UiLocalization.Text("GitRepositoryPublishForm.Button.Continue", "Continue(&O)"),
                UiLocalization.Text("GitRepositoryPublishForm.Accessible.Continue", "Continue"),
                6);
            _cancelButton = CreateButton(
                UiLocalization.Text("GitRepositoryPublishForm.Button.Cancel", "&Cancel"),
                UiLocalization.Text("GitRepositoryPublishForm.Accessible.Cancel", "Cancel"),
                7);
            UiTheme.ApplyButton(_okButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _okButton.Click += (sender, e) => AcceptValue();
            _cancelButton.Click += (sender, e) => DialogResult = DialogResult.Cancel;
            _githubRepositoryListView.ItemActivate += (sender, e) => AcceptValue();

            PopulateGitHubRepositories(githubRepositories);
            BuildLayout();
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            Shown += (sender, e) => FocusInitialControl();
        }

        public GitRepositoryPublishAction SelectedAction { get; private set; }

        public string RemoteUrl
        {
            get { return (_manualRemoteUrlTextBox.Text ?? string.Empty).Trim(); }
        }

        public GitHubCreateRepositoryRequest CreateRequest
        {
            get
            {
                return new GitHubCreateRepositoryRequest
                {
                    Name = (_createNameTextBox.Text ?? string.Empty).Trim(),
                    Description = (_createDescriptionTextBox.Text ?? string.Empty).Trim(),
                    IsPrivate = _createPrivateCheckBox.Checked,
                    AddReadme = false
                };
            }
        }

        private void AcceptValue()
        {
            if (_sourceTabControl.SelectedTab == _githubTabPage)
            {
                var repository = GetSelectedGitHubRepository();
                if (repository == null)
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("GitRepositoryPublishForm.Message.GitHubRepositoryRequired", "Select a GitHub repository first."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    _githubRepositoryListView.Focus();
                    return;
                }

                SelectedAction = GitRepositoryPublishAction.BindGitHubRepository;
                _manualRemoteUrlTextBox.Text = BuildGitHubRemoteUrl(repository);
                DialogResult = DialogResult.OK;
                return;
            }

            if (_sourceTabControl.SelectedTab == _createTabPage)
            {
                if (string.IsNullOrWhiteSpace(_createNameTextBox.Text))
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("GitRepositoryPublishForm.Message.NameRequired", "Enter a repository name first."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    _createNameTextBox.Focus();
                    return;
                }

                SelectedAction = GitRepositoryPublishAction.CreateGitHubRepository;
                DialogResult = DialogResult.OK;
                return;
            }

            if (string.IsNullOrWhiteSpace(_manualRemoteUrlTextBox.Text))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitRepositoryPublishForm.Message.RemoteUrlRequired", "Enter the remote URL first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _manualRemoteUrlTextBox.Focus();
                return;
            }

            SelectedAction = GitRepositoryPublishAction.BindManualRemote;
            DialogResult = DialogResult.OK;
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

            BuildGitHubTab();
            BuildCreateTab();
            BuildManualTab();
            _sourceTabControl.TabPages.Add(_githubTabPage);
            if (_allowGitHubCreate)
            {
                _sourceTabControl.TabPages.Add(_createTabPage);
            }
            _sourceTabControl.TabPages.Add(_manualTabPage);
            root.Controls.Add(_sourceTabControl, 0, 0);

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
            footer.Controls.Add(_okButton);
            root.Controls.Add(footer, 0, 1);
            Controls.Add(root);
        }

        private void BuildGitHubTab()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(CreateHintLabel(UiLocalization.Text("GitRepositoryPublishForm.Hint.GitHubEmptyRepositories", "Only empty repositories from the current GitHub CLI account are shown.")), 0, 0);
            root.Controls.Add(_githubRepositoryListView, 0, 1);
            _githubTabPage.Controls.Add(root);
        }

        private void BuildCreateTab()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(8),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            root.Controls.Add(CreateLabel(UiLocalization.Text("GitRepositoryPublishForm.Label.Name", "Repository name")), 0, 0);
            root.Controls.Add(_createNameTextBox, 0, 1);
            root.Controls.Add(CreateLabel(UiLocalization.Text("GitRepositoryPublishForm.Label.Description", "Description")), 0, 2);
            root.Controls.Add(_createDescriptionTextBox, 0, 3);
            root.Controls.Add(_createPrivateCheckBox, 0, 4);
            root.Controls.Add(CreateHintLabel(UiLocalization.Text("GitRepositoryPublishForm.Hint.CreateGitHubRepository", "The repository will be created on GitHub and bound as origin. Local commits are not pushed automatically.")), 0, 5);
            _createTabPage.Controls.Add(root);
        }

        private void BuildManualTab()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(8),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));

            root.Controls.Add(CreateLabel(UiLocalization.Text("GitRepositoryPublishForm.Label.RemoteUrl", "Remote URL")), 0, 0);
            root.Controls.Add(_manualRemoteUrlTextBox, 0, 1);
            root.Controls.Add(CreateHintLabel(UiLocalization.Text("GitRepositoryPublishForm.Hint.RemoteUrl", "Example: https://github.com/user/repo.git or git@github.com:user/repo.git")), 0, 2);
            _manualTabPage.Controls.Add(root);
        }

        private void FocusInitialControl()
        {
            if (_githubRepositoryListView.Items.Count > 0)
            {
                _sourceTabControl.SelectedTab = _githubTabPage;
                _githubRepositoryListView.Focus();
                return;
            }

            if (_allowGitHubCreate)
            {
                _sourceTabControl.SelectedTab = _createTabPage;
                _createNameTextBox.SelectAll();
                _createNameTextBox.Focus();
                return;
            }

            _sourceTabControl.SelectedTab = _manualTabPage;
            _manualRemoteUrlTextBox.SelectAll();
            _manualRemoteUrlTextBox.Focus();
        }

        private void PopulateGitHubRepositories(IReadOnlyList<GitHubRepositoryEntry> repositories)
        {
            _githubRepositoryListView.BeginUpdate();
            try
            {
                _githubRepositoryListView.Items.Clear();
                foreach (var repository in SortRepositories(repositories))
                {
                    var item = new ListViewItem(GetRepositoryName(repository));
                    item.SubItems.Add(DisplayRepositoryVisibility(repository));
                    item.Tag = repository;
                    _githubRepositoryListView.Items.Add(item);
                }

                if (_githubRepositoryListView.Items.Count > 0)
                {
                    _githubRepositoryListView.Items[0].Selected = true;
                    _githubRepositoryListView.Items[0].Focused = true;
                }
            }
            finally
            {
                _githubRepositoryListView.EndUpdate();
            }
        }

        private IEnumerable<GitHubRepositoryEntry> SortRepositories(IReadOnlyList<GitHubRepositoryEntry> repositories)
        {
            return (repositories ?? new GitHubRepositoryEntry[0])
                .Where(item => item != null)
                .OrderByDescending(item => string.Equals(GetRepositoryName(item), _preferredRepositoryName, StringComparison.OrdinalIgnoreCase))
                .ThenBy(item => GetRepositoryName(item), StringComparer.OrdinalIgnoreCase);
        }

        private GitHubRepositoryEntry GetSelectedGitHubRepository()
        {
            return _githubRepositoryListView.SelectedItems.Count == 0
                ? null
                : _githubRepositoryListView.SelectedItems[0].Tag as GitHubRepositoryEntry;
        }

        private static ListView CreateGitHubRepositoryListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitRepositoryPublishForm.Accessible.GitHubEmptyRepositories", "GitHub empty repositories"),
                TabIndex = 1,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            listView.Columns.Add(UiLocalization.Text("GitRepositoryPublishForm.Column.Repository", "Repository"), 390);
            listView.Columns.Add(UiLocalization.Text("GitRepositoryPublishForm.Column.Visibility", "Visibility"), 110);
            UiTheme.ApplyListView(listView);
            return listView;
        }

        private static string BuildGitHubRemoteUrl(GitHubRepositoryEntry repository)
        {
            if (repository == null)
            {
                return string.Empty;
            }

            var url = repository.HttpsUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = string.IsNullOrWhiteSpace(repository.NameWithOwner)
                    ? string.Empty
                    : "https://github.com/" + repository.NameWithOwner.Trim();
            }

            url = url.Trim();
            return url.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? url
                : url + ".git";
        }

        private static string GetRepositoryName(GitHubRepositoryEntry repository)
        {
            if (repository == null || string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                return string.Empty;
            }

            var name = repository.NameWithOwner.Trim();
            var slashIndex = name.IndexOf('/');
            return slashIndex >= 0 && slashIndex < name.Length - 1
                ? name.Substring(slashIndex + 1)
                : name;
        }

        private static string DisplayRepositoryVisibility(GitHubRepositoryEntry repository)
        {
            if (repository == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(repository.Visibility))
            {
                switch (repository.Visibility.Trim().ToUpperInvariant())
                {
                    case "PRIVATE":
                        return UiLocalization.Text("GitRepositoryPublishForm.Value.Private", "Private");
                    case "PUBLIC":
                        return UiLocalization.Text("GitRepositoryPublishForm.Value.Public", "Public");
                    default:
                        return repository.Visibility.Trim();
                }
            }

            return repository.IsPrivate
                ? UiLocalization.Text("GitRepositoryPublishForm.Value.Private", "Private")
                : UiLocalization.Text("GitRepositoryPublishForm.Value.Public", "Public");
        }

        private static Label CreateLabel(string text)
        {
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private static Label CreateHintLabel(string text)
        {
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private static TextBox CreateTextBox(string accessibleName, int tabIndex)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, false, false);
            return textBox;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 112,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }
    }
}
