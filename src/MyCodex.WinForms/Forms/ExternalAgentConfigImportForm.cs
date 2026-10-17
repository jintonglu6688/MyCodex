using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class ExternalAgentConfigImportForm : Form
    {
        private readonly IAppServerExternalAgentConfigClient _client;
        private readonly string _currentWorkspacePath;
        private readonly CheckBox _includeHomeCheckBox;
        private readonly CheckBox _includeCurrentWorkspaceCheckBox;
        private readonly ListView _migrationListView;
        private readonly Label _statusLabel;
        private readonly Button _detectButton;
        private readonly Button _selectAllButton;
        private readonly Button _clearSelectionButton;
        private readonly Button _importButton;
        private readonly Button _closeButton;

        private bool _isBusy;

        public ExternalAgentConfigImportForm(
            IAppServerExternalAgentConfigClient client,
            string currentWorkspacePath)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _currentWorkspacePath = string.IsNullOrWhiteSpace(currentWorkspacePath)
                ? string.Empty
                : currentWorkspacePath.Trim();

            Text = UiLocalization.Text("ExternalAgentConfigImportForm.Title", "Import External Agent Config");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(880, 560);
            Size = new Size(1000, 640);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _includeHomeCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("ExternalAgentConfigImportForm.Option.IncludeHome", "User configuration(&U)"),
                AccessibleName = UiLocalization.Text("ExternalAgentConfigImportForm.Accessible.IncludeHome", "User configuration"),
                Checked = true,
                AutoSize = true,
                UseMnemonic = true,
                TabIndex = 0
            };
            _includeCurrentWorkspaceCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("ExternalAgentConfigImportForm.Option.CurrentWorkspace", "Current workspace(&W)"),
                AccessibleName = UiLocalization.Text("ExternalAgentConfigImportForm.Accessible.CurrentWorkspace", "Current workspace"),
                Checked = !string.IsNullOrWhiteSpace(_currentWorkspacePath),
                Enabled = !string.IsNullOrWhiteSpace(_currentWorkspacePath),
                AutoSize = true,
                UseMnemonic = true,
                TabIndex = 1
            };
            _migrationListView = CreateMigrationListView();
            _migrationListView.TabIndex = 2;
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            _detectButton = CreateButton("ExternalAgentConfigImportForm.Button.Detect", "Detect(&D)", "ExternalAgentConfigImportForm.Accessible.Detect", "Detect", 3);
            _selectAllButton = CreateButton("ExternalAgentConfigImportForm.Button.SelectAll", "Select All(&A)", "ExternalAgentConfigImportForm.Accessible.SelectAll", "Select All", 4);
            _clearSelectionButton = CreateButton("ExternalAgentConfigImportForm.Button.ClearSelection", "Clear Selection(&N)", "ExternalAgentConfigImportForm.Accessible.ClearSelection", "Clear Selection", 5);
            _importButton = CreateButton("ExternalAgentConfigImportForm.Button.Import", "Import(&I)", "ExternalAgentConfigImportForm.Accessible.Import", "Import", 6);
            _closeButton = CreateButton("ExternalAgentConfigImportForm.Button.Close", "Close(&C)", "ExternalAgentConfigImportForm.Accessible.Close", "Close", 7);

            UiTheme.ApplyButton(_detectButton, UiGlyph.Search);
            UiTheme.ApplyButton(_selectAllButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_clearSelectionButton, UiGlyph.Cancel);
            UiTheme.ApplyButton(_importButton, UiGlyph.Download);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _detectButton.Click += DetectButton_Click;
            _selectAllButton.Click += SelectAllButton_Click;
            _clearSelectionButton.Click += ClearSelectionButton_Click;
            _importButton.Click += ImportButton_Click;
            _closeButton.Click += CloseButton_Click;
            _migrationListView.ItemChecked += MigrationListView_ItemChecked;
            _migrationListView.KeyDown += MigrationListView_KeyDown;
            KeyDown += ExternalAgentConfigImportForm_KeyDown;

            BuildLayout();
            AcceptButton = _importButton;
            CancelButton = _closeButton;
            ImportCompleted = false;
            SetStatus(UiLocalization.Text("ExternalAgentConfigImportForm.Status.Ready", "Ready."));
            RefreshActionButtons();
        }

        internal int MigrationItemCountForTests
        {
            get { return _migrationListView.Items.Count; }
        }

        internal bool ImportCompleted { get; private set; }

        internal string FirstMigrationItemTypeTextForTests
        {
            get { return _migrationListView.Items.Count == 0 ? null : _migrationListView.Items[0].Text; }
        }

        internal bool FirstMigrationItemSelectedForTests
        {
            get { return _migrationListView.Items.Count > 0 && _migrationListView.Items[0].Selected; }
        }

        internal Task LoadMigrationItemsForTestsAsync()
        {
            return LoadMigrationItemsAsync();
        }

        internal Task ImportSelectedItemsForTestsAsync()
        {
            return ImportSelectedItemsAsync();
        }

        private async void DetectButton_Click(object sender, EventArgs e)
        {
            await LoadMigrationItemsAsync();
        }

        private void SelectAllButton_Click(object sender, EventArgs e)
        {
            SetAllCheckedState(true);
        }

        private void ClearSelectionButton_Click(object sender, EventArgs e)
        {
            SetAllCheckedState(false);
        }

        private async void ImportButton_Click(object sender, EventArgs e)
        {
            await ImportSelectedItemsAsync();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            DialogResult = ImportCompleted ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }

        private void MigrationListView_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            RefreshActionButtons();
        }

        private async void MigrationListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !_isBusy && _migrationListView.CheckedItems.Count > 0)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await ImportSelectedItemsAsync();
            }
        }

        private async void ExternalAgentConfigImportForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CloseButton_Click(_closeButton, EventArgs.Empty);
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await LoadMigrationItemsAsync();
            }
        }

        private async Task LoadMigrationItemsAsync()
        {
            if (_isBusy)
            {
                return;
            }

            SetBusy(true);
            ImportCompleted = false;
            var shouldFocusMigrationList = false;
            SetStatus(UiLocalization.Text("ExternalAgentConfigImportForm.Status.Detecting", "Detecting external agent configuration..."));

            try
            {
                var result = await _client.DetectExternalAgentConfigAsync(
                    GetSelectedWorkingDirectories(),
                    _includeHomeCheckBox.Checked,
                    CancellationToken.None).ConfigureAwait(true);
                BindMigrationItems(result == null ? null : result.Items);
                SetStatus(_migrationListView.Items.Count == 0
                    ? UiLocalization.Text("ExternalAgentConfigImportForm.Status.NoItems", "No external agent configuration items were detected.")
                    : UiLocalization.Text("ExternalAgentConfigImportForm.Status.SelectItems", "Select items to import."));
                shouldFocusMigrationList = _migrationListView.Items.Count > 0;
            }
            catch (Exception ex)
            {
                BindMigrationItems(null);
                SetStatus(UiLocalization.Text("ExternalAgentConfigImportForm.Status.DetectFailed", "Failed to detect external agent configuration."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
                if (shouldFocusMigrationList)
                {
                    FocusMigrationList();
                }
            }
        }

        private async Task ImportSelectedItemsAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var selectedItems = GetCheckedMigrationItems();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("ExternalAgentConfigImportForm.Message.SelectAtLeastOne", "Select at least one item to import."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            SetBusy(true);
            SetStatus(UiLocalization.Text("ExternalAgentConfigImportForm.Status.Importing", "Importing external agent configuration..."));

            try
            {
                await _client.ImportExternalAgentConfigAsync(selectedItems, CancellationToken.None).ConfigureAwait(true);
                ImportCompleted = true;
                DialogResult = DialogResult.OK;
                SetStatus(string.Format(
                    UiLocalization.Text("ExternalAgentConfigImportForm.Status.ImportCompleted.Template", "Imported {0} external agent configuration item(s)."),
                    selectedItems.Count));
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("ExternalAgentConfigImportForm.Status.ImportFailed", "Failed to import external agent configuration."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void BindMigrationItems(IEnumerable<ExternalAgentConfigMigrationItem> items)
        {
            _migrationListView.BeginUpdate();
            try
            {
                _migrationListView.Items.Clear();
                if (items == null)
                {
                    return;
                }

                foreach (var migrationItem in items.Where(item => item != null && item.ItemType != ExternalAgentConfigMigrationItemType.Config))
                {
                    var listItem = new ListViewItem(new[]
                    {
                        GetItemTypeDisplayText(migrationItem),
                        GetScopeDisplayText(migrationItem),
                        migrationItem.Description ?? string.Empty,
                        GetDetailsDisplayText(migrationItem)
                    })
                    {
                        Checked = ExternalAgentConfigImportPolicy.CanImport(migrationItem),
                        Tag = migrationItem
                    };
                    _migrationListView.Items.Add(listItem);
                }
            }
            finally
            {
                _migrationListView.EndUpdate();
                RefreshActionButtons();
            }
        }

        private List<ExternalAgentConfigMigrationItem> GetCheckedMigrationItems()
        {
            var result = new List<ExternalAgentConfigMigrationItem>();
            foreach (ListViewItem item in _migrationListView.Items)
            {
                if (!item.Checked)
                {
                    continue;
                }

                var migrationItem = item.Tag as ExternalAgentConfigMigrationItem;
                if (ExternalAgentConfigImportPolicy.CanImport(migrationItem))
                {
                    result.Add(migrationItem);
                }
            }

            return result;
        }

        private IReadOnlyCollection<string> GetSelectedWorkingDirectories()
        {
            if (!_includeCurrentWorkspaceCheckBox.Checked || string.IsNullOrWhiteSpace(_currentWorkspacePath))
            {
                return new string[0];
            }

            return new[] { _currentWorkspacePath };
        }

        private void SetAllCheckedState(bool isChecked)
        {
            foreach (ListViewItem item in _migrationListView.Items)
            {
                var migrationItem = item.Tag as ExternalAgentConfigMigrationItem;
                item.Checked = isChecked && ExternalAgentConfigImportPolicy.CanImport(migrationItem);
            }

            RefreshActionButtons();
        }

        private void FocusMigrationList()
        {
            if (_migrationListView.Items.Count == 0)
            {
                return;
            }

            if (_migrationListView.SelectedItems.Count == 0)
            {
                _migrationListView.Items[0].Selected = true;
            }

            _migrationListView.Items[0].EnsureVisible();
            if (_migrationListView.CanFocus)
            {
                _migrationListView.Focus();
            }
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            _includeHomeCheckBox.Enabled = !busy;
            _includeCurrentWorkspaceCheckBox.Enabled = !busy && !string.IsNullOrWhiteSpace(_currentWorkspacePath);
            _migrationListView.Enabled = !busy;
            _detectButton.Enabled = !busy;
            _selectAllButton.Enabled = !busy && _migrationListView.Items.Count > 0;
            _clearSelectionButton.Enabled = !busy && _migrationListView.Items.Count > 0;
            _importButton.Enabled = !busy && _migrationListView.CheckedItems.Count > 0;
            _closeButton.Enabled = true;
        }

        private void RefreshActionButtons()
        {
            if (_isBusy)
            {
                return;
            }

            _selectAllButton.Enabled = _migrationListView.Items.Count > 0;
            _clearSelectionButton.Enabled = _migrationListView.Items.Count > 0;
            _importButton.Enabled = _migrationListView.CheckedItems.Count > 0;
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text ?? string.Empty;
        }

        private void BuildLayout()
        {
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                ColumnCount = 1,
                RowCount = 3,
                TabStop = false
            };
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));

            var optionsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            optionsPanel.Controls.Add(_includeHomeCheckBox);
            optionsPanel.Controls.Add(_includeCurrentWorkspaceCheckBox);

            var footerPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                ColumnCount = 2,
                RowCount = 1,
                TabStop = false
            };
            footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470F));

            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            buttonsPanel.Controls.Add(_closeButton);
            buttonsPanel.Controls.Add(_importButton);
            buttonsPanel.Controls.Add(_clearSelectionButton);
            buttonsPanel.Controls.Add(_selectAllButton);
            buttonsPanel.Controls.Add(_detectButton);

            footerPanel.Controls.Add(_statusLabel, 0, 0);
            footerPanel.Controls.Add(buttonsPanel, 1, 0);

            mainPanel.Controls.Add(optionsPanel, 0, 0);
            mainPanel.Controls.Add(_migrationListView, 0, 1);
            mainPanel.Controls.Add(footerPanel, 0, 2);

            Controls.Add(mainPanel);
        }

        private static Button CreateButton(
            string textKey,
            string textFallback,
            string accessibleKey,
            string accessibleFallback,
            int tabIndex)
        {
            return new Button
            {
                Text = UiLocalization.Text(textKey, textFallback),
                AccessibleName = UiLocalization.Text(accessibleKey, accessibleFallback),
                Width = 94,
                Height = 32,
                Margin = new Padding(8, 8, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }

        private static ListView CreateMigrationListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                AccessibleName = UiLocalization.Text("ExternalAgentConfigImportForm.Accessible.Migrations", "External agent configuration items"),
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                UseCompatibleStateImageBehavior = false,
                LabelWrap = false
            };
            UiTheme.ApplyListView(listView);

            listView.Columns.Add(UiLocalization.Text("ExternalAgentConfigImportForm.Column.Type", "Type"), 150, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ExternalAgentConfigImportForm.Column.Scope", "Scope"), 220, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ExternalAgentConfigImportForm.Column.Description", "Description"), 300, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ExternalAgentConfigImportForm.Column.Details", "Details"), 260, HorizontalAlignment.Left);
            return listView;
        }

        private static string GetItemTypeDisplayText(ExternalAgentConfigMigrationItem item)
        {
            return string.IsNullOrWhiteSpace(item == null ? null : item.RawItemType)
                ? UiLocalization.Text("ExternalAgentConfigImportForm.Value.External", "External")
                : item.RawItemType;
        }

        private static string GetScopeDisplayText(ExternalAgentConfigMigrationItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Cwd))
            {
                return UiLocalization.Text("ExternalAgentConfigImportForm.Scope.UserConfiguration", "User configuration");
            }

            return item.Cwd;
        }

        private static string GetDetailsDisplayText(ExternalAgentConfigMigrationItem item)
        {
            if (item == null || item.Details == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            AppendNames(builder, item.Details.Subagents, "subagents");
            AppendNames(builder, item.Details.Commands, "commands");
            AppendNames(builder, item.Details.Hooks, "hooks");
            AppendNames(builder, item.Details.McpServers, "mcp");
            AppendPlugins(builder, item.Details.Plugins);
            AppendSessions(builder, item.Details.Sessions);
            return builder.ToString();
        }

        private static void AppendNames(StringBuilder builder, IEnumerable<ExternalAgentConfigNamedMigration> items, string label)
        {
            var names = items == null
                ? new string[0]
                : items.Select(item => item == null ? null : item.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .ToArray();
            if (names.Length == 0)
            {
                return;
            }

            AppendPart(builder, label + ": " + string.Join(", ", names));
        }

        private static void AppendPlugins(StringBuilder builder, IEnumerable<ExternalAgentConfigPluginMigration> plugins)
        {
            if (plugins == null)
            {
                return;
            }

            foreach (var plugin in plugins)
            {
                if (plugin == null)
                {
                    continue;
                }

                var names = plugin.PluginNames == null
                    ? string.Empty
                    : string.Join(", ", plugin.PluginNames.Where(name => !string.IsNullOrWhiteSpace(name)));
                if (!string.IsNullOrWhiteSpace(plugin.MarketplaceName) || !string.IsNullOrWhiteSpace(names))
                {
                    AppendPart(builder, "plugins: " + plugin.MarketplaceName + (string.IsNullOrWhiteSpace(names) ? string.Empty : "/" + names));
                }
            }
        }

        private static void AppendSessions(StringBuilder builder, IEnumerable<ExternalAgentConfigSessionMigration> sessions)
        {
            if (sessions == null)
            {
                return;
            }

            var titles = sessions
                .Where(item => item != null)
                .Select(item => string.IsNullOrWhiteSpace(item.Title) ? item.Path : item.Title)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
            if (titles.Length > 0)
            {
                AppendPart(builder, "sessions: " + string.Join(", ", titles));
            }
        }

        private static void AppendPart(StringBuilder builder, string text)
        {
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(text);
        }
    }
}
