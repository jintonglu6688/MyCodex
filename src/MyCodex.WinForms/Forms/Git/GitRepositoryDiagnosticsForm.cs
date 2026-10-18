using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitRepositoryDiagnosticsForm : Form, IRuntimeTransitionWindow
    {
        bool IRuntimeTransitionWindow.IsBusyForRuntimeTransition => _isLoading || _isRunningMaintenance;

        private readonly string _workspacePath;
        private readonly IGitRepositoryMaintenanceService _gitService;
        private readonly Label _statusLabel;
        private readonly TabControl _tabControl;
        private readonly ListView _diagnosticListView;
        private readonly TextBox _detailsTextBox;
        private readonly TextBox _maintenanceOutputTextBox;
        private readonly Button _refreshButton;
        private readonly Button _integrityCheckButton;
        private readonly Button _maintenanceButton;
        private readonly Button _closeButton;
        private bool _isLoading;
        private bool _isRunningMaintenance;

        public GitRepositoryDiagnosticsForm(string workspacePath, IGitRepositoryMaintenanceService gitService)
        {
            _workspacePath = workspacePath;
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));

            Text = UiLocalization.Text("GitRepositoryDiagnosticsForm.Title", "Repository Diagnostics and Maintenance");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 460);
            Size = new Size(900, 560);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, true);

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                AccessibleName = string.Empty,
                TabIndex = 0
            };
            _diagnosticListView = CreateDiagnosticListView();
            _detailsTextBox = CreateDetailsTextBox();
            _maintenanceOutputTextBox = CreateMaintenanceOutputTextBox();
            _refreshButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Button.Refresh", "Refresh(&R)"),
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Accessible.Refresh", "Refresh"),
                1,
                118);
            _integrityCheckButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Button.IntegrityCheck", "Check Integrity(&F)"),
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Accessible.IntegrityCheck", "Check integrity"),
                1,
                150);
            _maintenanceButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Button.Maintenance", "Run Maintenance(&M)"),
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Accessible.Maintenance", "Run maintenance"),
                2,
                150);
            _closeButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Button.Close", "Close(&E)"),
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Accessible.Close", "Close"),
                2,
                118);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_integrityCheckButton, UiGlyph.Info);
            UiTheme.ApplyButton(_maintenanceButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _refreshButton.Click += async (sender, e) => await LoadDiagnosticsAsync();
            _integrityCheckButton.Click += async (sender, e) => await RunIntegrityCheckAsync();
            _maintenanceButton.Click += async (sender, e) => await RunMaintenanceAsync();
            _closeButton.Click += (sender, e) => Close();
            _diagnosticListView.SelectedIndexChanged += (sender, e) => UpdateDetailsText();
            KeyDown += GitRepositoryDiagnosticsForm_KeyDown;
            Shown += async (sender, e) =>
            {
                Activate();
                await LoadDiagnosticsAsync();
            };

            BuildLayout();
            CancelButton = _closeButton;
            SetStatus(UiLocalization.Text("GitRepositoryDiagnosticsForm.Status.Preparing", "Preparing diagnostics..."));
            TextBoxCaretHelper.SetTextAndMoveToStart(
                _maintenanceOutputTextBox,
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Value.InitialMaintenanceOutput", "No maintenance action has run yet."));
        }

        private async void GitRepositoryDiagnosticsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_isLoading || _isRunningMaintenance)
            {
                return;
            }

            if (e.KeyCode == Keys.F5 && IsDiagnosticsTabSelected())
            {
                e.SuppressKeyPress = true;
                await LoadDiagnosticsAsync();
            }
        }

        private async Task LoadDiagnosticsAsync()
        {
            if (_isLoading || _isRunningMaintenance)
            {
                return;
            }

            _isLoading = true;
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitRepositoryDiagnosticsForm.Status.Loading", "Running repository diagnostics..."));
                var entries = await _gitService.RunRepositoryDiagnosticsAsync(_workspacePath).ConfigureAwait(true)
                    ?? new GitRepositoryDiagnosticEntry[0];
                BindDiagnostics(entries);
                SetStatus(string.Format(
                    UiLocalization.Text("GitRepositoryDiagnosticsForm.Status.Loaded.Template", "Loaded {0} diagnostic item(s)."),
                    entries.Count));
            }
            catch (Exception ex)
            {
                BindDiagnostics(new GitRepositoryDiagnosticEntry[0]);
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
                RestoreFocus();
            }
        }

        private void BindDiagnostics(IReadOnlyList<GitRepositoryDiagnosticEntry> entries)
        {
            _diagnosticListView.BeginUpdate();
            try
            {
                _diagnosticListView.Items.Clear();
                foreach (var entry in entries ?? new GitRepositoryDiagnosticEntry[0])
                {
                    var item = new ListViewItem(GetSeverityText(entry == null ? null : entry.Severity));
                    item.SubItems.Add(entry == null ? string.Empty : entry.Title ?? string.Empty);
                    item.SubItems.Add(entry == null ? string.Empty : entry.Detail ?? string.Empty);
                    item.Tag = entry;
                    _diagnosticListView.Items.Add(item);
                }
            }
            finally
            {
                _diagnosticListView.EndUpdate();
            }

            if (_diagnosticListView.Items.Count > 0)
            {
                _diagnosticListView.Items[0].Selected = true;
                _diagnosticListView.Items[0].Focused = true;
                _diagnosticListView.EnsureVisible(0);
            }

            UpdateDetailsText();
        }

        private void UpdateDetailsText()
        {
            var entry = GetSelectedEntry();
            if (entry == null)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(
                    _detailsTextBox,
                    UiLocalization.Text("GitRepositoryDiagnosticsForm.Value.SelectItem", "Select a diagnostic item to view details."));
                return;
            }

            var text = string.Format(
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Value.Detail.Template", "Status: {0}\r\nItem: {1}\r\n\r\nDetail:\r\n{2}\r\n\r\nSuggestion:\r\n{3}"),
                GetSeverityText(entry.Severity),
                entry.Title ?? string.Empty,
                string.IsNullOrWhiteSpace(entry.Detail) ? UiLocalization.Text("GitRepositoryDiagnosticsForm.Value.NoDetail", "(No details)") : entry.Detail,
                string.IsNullOrWhiteSpace(entry.Suggestion) ? UiLocalization.Text("GitRepositoryDiagnosticsForm.Value.NoSuggestion", "(No suggestion)") : entry.Suggestion);
            TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, text);
        }

        private GitRepositoryDiagnosticEntry GetSelectedEntry()
        {
            return _diagnosticListView.SelectedItems.Count == 0
                ? null
                : _diagnosticListView.SelectedItems[0].Tag as GitRepositoryDiagnosticEntry;
        }

        private void RefreshButtons()
        {
            _refreshButton.Enabled = !_isLoading && !_isRunningMaintenance && IsDiagnosticsTabSelected();
            _integrityCheckButton.Enabled = !_isLoading && !_isRunningMaintenance;
            _maintenanceButton.Enabled = !_isLoading && !_isRunningMaintenance;
            _closeButton.Enabled = !_isLoading && !_isRunningMaintenance;
            _diagnosticListView.Enabled = !_isLoading || _diagnosticListView.Items.Count > 0;
            _detailsTextBox.Enabled = true;
            _maintenanceOutputTextBox.Enabled = true;
        }

        private void RestoreFocus()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                Activate();
                if (_diagnosticListView.Items.Count > 0 && _diagnosticListView.CanFocus)
                {
                    _diagnosticListView.Focus();
                    return;
                }

                if (_closeButton.CanFocus)
                {
                    _closeButton.Focus();
                }
            });
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text ?? string.Empty;
        }

        private async Task RunIntegrityCheckAsync()
        {
            await RunMaintenanceOperationAsync(
                () => _gitService.RunRepositoryIntegrityCheckAsync(_workspacePath),
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Status.RunningIntegrityCheck", "Checking repository integrity..."));
        }

        private async Task RunMaintenanceAsync()
        {
            var result = MessageBox.Show(
                this,
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Confirm.Maintenance", "Run Git maintenance now?\r\n\r\nThis uses git maintenance run. It is normally safe, but it may take time on large repositories."),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreMaintenanceFocus();
                return;
            }

            await RunMaintenanceOperationAsync(
                () => _gitService.RunRepositoryMaintenanceAsync(_workspacePath),
                UiLocalization.Text("GitRepositoryDiagnosticsForm.Status.RunningMaintenance", "Running repository maintenance..."));
        }

        private async Task RunMaintenanceOperationAsync(Func<Task<GitOperationResult>> operation, string runningStatus)
        {
            if (_isLoading || _isRunningMaintenance || operation == null)
            {
                return;
            }

            _isRunningMaintenance = true;
            try
            {
                RefreshButtons();
                SetStatus(runningStatus);
                TextBoxCaretHelper.SetTextAndMoveToStart(_maintenanceOutputTextBox, runningStatus);
                var result = await operation().ConfigureAwait(true);
                var output = BuildOperationOutput(result);
                TextBoxCaretHelper.SetTextAndMoveToStart(_maintenanceOutputTextBox, output);
                SetStatus(result != null && result.Success
                    ? result.DisplayMessage
                    : UiLocalization.Text("GitRepositoryDiagnosticsForm.Status.MaintenanceFailed", "Maintenance action failed."));
                if (result != null && !result.Success)
                {
                    MessageBox.Show(this, result.DisplayMessage, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_maintenanceOutputTextBox, ex.Message);
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isRunningMaintenance = false;
                RefreshButtons();
                RestoreMaintenanceFocus();
            }
        }

        private void RestoreMaintenanceFocus()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                Activate();
                if (_maintenanceOutputTextBox.CanFocus)
                {
                    _maintenanceOutputTextBox.Focus();
                }
            });
        }

        private bool IsDiagnosticsTabSelected()
        {
            return _tabControl == null || _tabControl.SelectedIndex == 0;
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _tabControl.TabPages.Add(CreateDiagnosticsTabPage());
            _tabControl.TabPages.Add(CreateMaintenanceTabPage());
            _tabControl.SelectedIndexChanged += (sender, e) => RefreshButtons();

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_refreshButton);
            footer.Controls.Add(_closeButton);

            root.Controls.Add(_statusLabel, 0, 0);
            root.Controls.Add(_tabControl, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
        }

        private TabPage CreateDiagnosticsTabPage()
        {
            var tabPage = new TabPage(UiLocalization.Text("GitRepositoryDiagnosticsForm.Tab.Diagnostics", "Diagnostics"))
            {
                BackColor = UiTheme.WindowBackColor
            };

            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 220,
                SplitterWidth = 6,
                TabStop = false
            };
            splitContainer.Panel1.Controls.Add(_diagnosticListView);
            splitContainer.Panel2.Controls.Add(_detailsTextBox);
            tabPage.Controls.Add(splitContainer);
            return tabPage;
        }

        private TabPage CreateMaintenanceTabPage()
        {
            var tabPage = new TabPage(UiLocalization.Text("GitRepositoryDiagnosticsForm.Tab.Maintenance", "Maintenance"))
            {
                BackColor = UiTheme.WindowBackColor
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_integrityCheckButton);
            footer.Controls.Add(_maintenanceButton);

            root.Controls.Add(_maintenanceOutputTextBox, 0, 0);
            root.Controls.Add(footer, 0, 1);
            tabPage.Controls.Add(root);
            return tabPage;
        }

        private static ListView CreateDiagnosticListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details,
                AccessibleName = UiLocalization.Text("GitRepositoryDiagnosticsForm.Accessible.Diagnostics", "Diagnostics"),
                TabIndex = 0,
                UseCompatibleStateImageBehavior = false
            };
            UiTheme.ApplyListView(listView);
            listView.Columns.Add(UiLocalization.Text("GitRepositoryDiagnosticsForm.Column.Status", "Status"), 90);
            listView.Columns.Add(UiLocalization.Text("GitRepositoryDiagnosticsForm.Column.Item", "Item"), 180);
            listView.Columns.Add(UiLocalization.Text("GitRepositoryDiagnosticsForm.Column.Detail", "Detail"), 560);
            return listView;
        }

        private static TextBox CreateDetailsTextBox()
        {
            var textBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("GitRepositoryDiagnosticsForm.Accessible.Details", "Details"),
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = true,
                TabIndex = 1
            };
            UiTheme.ApplyTextBox(textBox, true, false);
            return textBox;
        }

        private static TextBox CreateMaintenanceOutputTextBox()
        {
            var textBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("GitRepositoryDiagnosticsForm.Accessible.MaintenanceOutput", "Maintenance output"),
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = true,
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(textBox, true, false);
            return textBox;
        }

        private static string GetSeverityText(string severity)
        {
            switch ((severity ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "ok":
                    return UiLocalization.Text("GitRepositoryDiagnosticsForm.Value.Severity.Ok", "OK");
                case "warning":
                    return UiLocalization.Text("GitRepositoryDiagnosticsForm.Value.Severity.Warning", "Warning");
                case "error":
                    return UiLocalization.Text("GitRepositoryDiagnosticsForm.Value.Severity.Error", "Error");
                default:
                    return severity ?? string.Empty;
            }
        }

        private static string BuildOperationOutput(GitOperationResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            var output = FirstNonEmpty(result.StandardOutput, result.StandardError);
            return string.IsNullOrWhiteSpace(output)
                ? result.DisplayMessage
                : result.DisplayMessage + Environment.NewLine + output;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i];
                }
            }

            return string.Empty;
        }
    }
}
