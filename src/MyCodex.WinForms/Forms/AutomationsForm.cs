using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class AutomationsForm : Form
    {
        private readonly IAutomationStore _store;
        private readonly IAutomationService _automationService;
        private readonly IAutomationRunner _automationRunner;
        private readonly string _defaultWorkspacePath;
        private readonly AutomationInputSelectionCallbacks _inputSelectionCallbacks;
        private readonly AutomationRunActionCallbacks _runActionCallbacks;
        private readonly AutomationModelSelectionCallbacks _modelSelectionCallbacks;
        private readonly AutomationRunActivityCallbacks _runActivityCallbacks;
        private readonly TabControl _tabs;
        private readonly ListView _automationsListView;
        private readonly ListView _runsListView;
        private readonly Label _statusLabel;
        private readonly Button _newButton;
        private readonly Button _assignPendingButton;
        private AutomationCreationContext _targetCreationContext;

        internal Action AssignPendingAutomations { get; set; }
        private readonly Button _runNowButton;
        private readonly Button _detailsButton;
        private readonly Button _openThreadButton;
        private readonly Button _stopRunButton;
        private readonly ContextMenu _automationContextMenu;
        private readonly MenuItem _editAutomationMenuItem;
        private readonly MenuItem _toggleAutomationEnabledMenuItem;
        private readonly MenuItem _deleteAutomationMenuItem;
        private readonly ContextMenu _runContextMenu;
        private readonly MenuItem _deleteRunMenuItem;

        internal Func<AutomationDefinitionSnapshot, bool> ConfirmDeleteAutomation { get; set; }

        internal Func<AutomationRunSnapshot, bool> ConfirmDeleteRun { get; set; }

        internal Func<AutomationRunSnapshot, AutomationRunDeleteConfirmation> ConfirmDeleteRunOptions { get; set; }

        public AutomationsForm(
            IAutomationStore store,
            IAutomationService automationService,
            string defaultWorkspacePath = null,
            AutomationInputSelectionCallbacks inputSelectionCallbacks = null,
            IAutomationRunner automationRunner = null,
            AutomationRunActionCallbacks runActionCallbacks = null,
            AutomationModelSelectionCallbacks modelSelectionCallbacks = null,
            AutomationRunActivityCallbacks runActivityCallbacks = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _automationService = automationService;
            _automationRunner = automationRunner;
            _defaultWorkspacePath = string.IsNullOrWhiteSpace(defaultWorkspacePath) ? string.Empty : defaultWorkspacePath.Trim();
            _inputSelectionCallbacks = inputSelectionCallbacks;
            _runActionCallbacks = runActionCallbacks;
            _modelSelectionCallbacks = modelSelectionCallbacks;
            _runActivityCallbacks = runActivityCallbacks;

            Text = UiLocalization.Text("AutomationsForm.Title", "Automations");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            Size = new Size(980, 640);
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Name = "automationsTabs",
                TabIndex = 0
            };
            _automationsListView = CreateListView(UiLocalization.Text("AutomationsForm.Accessible.Automations", "Automations"));
            AddAutomationColumns(_automationsListView);
            _runsListView = CreateListView(UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "Run results"));
            AddDefaultColumns(_runsListView);
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TabStop = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            UiTheme.ApplyLabel(_statusLabel, false);

            _newButton = CreateButton(UiLocalization.Text("AutomationsForm.Button.New", "New(&N)"), UiLocalization.Text("AutomationsForm.Accessible.New", "New"), 1);
            _runNowButton = CreateButton(UiLocalization.Text("AutomationsForm.Button.RunNow", "Run Now(&R)"), UiLocalization.Text("AutomationsForm.Accessible.RunNow", "Run now"), 2);
            _detailsButton = CreateButton(UiLocalization.Text("AutomationsForm.Button.Details", "Details(&D)"), UiLocalization.Text("AutomationsForm.Accessible.Details", "Details"), 3, UiGlyph.Info);
            _openThreadButton = CreateButton(UiLocalization.Text("AutomationsForm.Button.OpenThread", "Open thread(&O)"), UiLocalization.Text("AutomationsForm.Accessible.OpenThread", "Open thread"), 4, UiGlyph.Info);
            _stopRunButton = CreateButton(UiLocalization.Text("AutomationsForm.Button.StopRun", "Stop run(&T)"), UiLocalization.Text("AutomationsForm.Accessible.StopRun", "Stop run"), 5, UiGlyph.Stop);
            _assignPendingButton = CreateButton(UiLocalization.Text("AutomationsForm.Button.AssignPending", "Assign old tasks(&P)..."),
                UiLocalization.Text("AutomationsForm.Accessible.AssignPending", "Assign old tasks"), 6);
            _assignPendingButton.Click += (sender, e) =>
            {
                AssignPendingAutomations?.Invoke();
                RefreshData();
            };
            _automationContextMenu = new ContextMenu();
            _editAutomationMenuItem = new MenuItem(UiLocalization.Text("AutomationsForm.Menu.EditAutomation", "Edit(&E)..."));
            _toggleAutomationEnabledMenuItem = new MenuItem(UiLocalization.Text("AutomationsForm.Menu.DisableAutomation", "Disable(&B)"));
            _deleteAutomationMenuItem = new MenuItem(UiLocalization.Text("AutomationsForm.Menu.DeleteAutomation", "Delete(&D)"));
            _automationContextMenu.MenuItems.Add(_editAutomationMenuItem);
            _automationContextMenu.MenuItems.Add(_toggleAutomationEnabledMenuItem);
            _automationContextMenu.MenuItems.Add(_deleteAutomationMenuItem);
            _runContextMenu = new ContextMenu();
            _deleteRunMenuItem = new MenuItem(UiLocalization.Text("AutomationsForm.Menu.DeleteRun", "Delete result(&D)"));
            _runContextMenu.MenuItems.Add(_deleteRunMenuItem);
            _automationsListView.ContextMenu = _automationContextMenu;
            _runsListView.ContextMenu = _runContextMenu;
            _newButton.Click += NewButton_Click;
            _runNowButton.Click += RunNowButton_Click;
            _detailsButton.Click += DetailsButton_Click;
            _openThreadButton.Click += OpenThreadButton_Click;
            _stopRunButton.Click += StopRunButton_Click;
            _editAutomationMenuItem.Click += EditAutomationMenuItem_Click;
            _toggleAutomationEnabledMenuItem.Click += ToggleAutomationEnabledMenuItem_Click;
            _deleteAutomationMenuItem.Click += DeleteAutomationMenuItem_Click;
            _deleteRunMenuItem.Click += DeleteRunMenuItem_Click;
            _automationContextMenu.Popup += AutomationContextMenu_Popup;
            _runContextMenu.Popup += RunContextMenu_Popup;
            _tabs.SelectedIndexChanged += (sender, e) => UpdateActionButtons();
            _automationsListView.SelectedIndexChanged += (sender, e) => UpdateActionButtons();
            _runsListView.SelectedIndexChanged += (sender, e) => UpdateActionButtons();
            _runsListView.DoubleClick += RunListView_OpenDetails;
            _runsListView.KeyDown += RunListView_KeyDown;
            _automationsListView.MouseDown += ListView_SelectItemOnRightClick;
            _runsListView.MouseDown += ListView_SelectItemOnRightClick;

            BuildLayout();
            RefreshData();
            UpdateActionButtons();
        }

        public void RefreshAutomationState()
        {
            RefreshData(UiLocalization.Text("AutomationsForm.Status.Updated", "Automation state updated."));
        }

        internal void SetTargetCreationContext(AutomationCreationContext context)
        {
            _targetCreationContext = context;
            UpdateActionButtons();
        }

        public void ShowRunResults()
        {
            RefreshData(UiLocalization.Text("AutomationsForm.Status.Updated", "Automation state updated."));
            if (_tabs.TabPages.Count > 1)
            {
                _tabs.SelectedIndex = 1;
            }

            _runsListView.Focus();
            UpdateActionButtons();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateActionButtons();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            if (keyData == Keys.F5)
            {
                RefreshData(UiLocalization.Text("AutomationsForm.Status.Refreshed", "Automations refreshed."));
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

            _tabs.TabPages.Add(CreateTab(UiLocalization.Text("AutomationsForm.Tab.Automations", "Automations"), _automationsListView));
            _tabs.TabPages.Add(CreateTab(UiLocalization.Text("AutomationsForm.Tab.RunHistory", "Run results"), _runsListView));

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                TabStop = false
            };
            buttons.Controls.Add(_newButton);
            buttons.Controls.Add(_runNowButton);
            buttons.Controls.Add(_detailsButton);
            buttons.Controls.Add(_openThreadButton);
            buttons.Controls.Add(_stopRunButton);
            buttons.Controls.Add(_assignPendingButton);

            root.Controls.Add(_tabs, 0, 0);
            root.Controls.Add(buttons, 0, 1);
            root.Controls.Add(_statusLabel, 0, 2);
            Controls.Add(root);
        }

        private async void NewButton_Click(object sender, EventArgs e)
        {
            if (_automationService == null)
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.ServiceUnavailable", "Automation service is unavailable.");
                return;
            }

            _newButton.Enabled = false;
            try
            {
                var modelOptions = await LoadAutomationModelOptionsForEditAsync().ConfigureAwait(true);
                using (var form = new AutomationEditForm(_targetCreationContext ?? new AutomationCreationContext
                {
                    Kind = AutomationKind.Standalone,
                    WorkspacePath = _defaultWorkspacePath
                }, _inputSelectionCallbacks, modelOptions, GetSupportsReasoningEffortForModelCallback()))
                {
                    if (form.ShowDialog(this) != DialogResult.OK || form.Request == null)
                    {
                        return;
                    }

                    _automationService.Create(form.Request);
                    RefreshData();
                }
            }
            finally
            {
                UpdateActionButtons();
            }
        }

        private async Task<IReadOnlyList<AppServerModelOption>> LoadAutomationModelOptionsForEditAsync()
        {
            if (_modelSelectionCallbacks == null || _modelSelectionCallbacks.LoadModelsAsync == null)
            {
                return new List<AppServerModelOption>();
            }

            try
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.LoadingModels", "Loading model choices...");
                var models = await _modelSelectionCallbacks.LoadModelsAsync().ConfigureAwait(true) ?? new List<AppServerModelOption>();
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.Ready", "Automations loaded.");
                return models;
            }
            catch (Exception ex)
            {
                _statusLabel.Text = string.Format(
                    UiLocalization.Text("AutomationsForm.Status.LoadModelChoicesFailed", "Failed to load model choices: {0}"),
                    ex.Message);
                return new List<AppServerModelOption>();
            }
        }

        private Func<string, bool> GetSupportsReasoningEffortForModelCallback()
        {
            return _modelSelectionCallbacks != null
                ? _modelSelectionCallbacks.SupportsReasoningEffortForModel
                : null;
        }

        private async void EditAutomationMenuItem_Click(object sender, EventArgs e)
        {
            await EditSelectedAutomationAsync();
        }

        private async Task EditSelectedAutomationAsync()
        {
            var definition = GetSelectedAutomation();
            if (definition == null)
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.SelectAutomation", "Select an automation first.");
                return;
            }

            if (_automationService == null)
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.ServiceUnavailable", "Automation service is unavailable.");
                return;
            }

            _editAutomationMenuItem.Enabled = false;
            try
            {
                var modelOptions = await LoadAutomationModelOptionsForEditAsync().ConfigureAwait(true);
                using (var form = new AutomationEditForm(
                    definition,
                    _inputSelectionCallbacks,
                    modelOptions,
                    GetSupportsReasoningEffortForModelCallback()))
                {
                    if (form.ShowDialog(this) != DialogResult.OK || form.Request == null)
                    {
                        return;
                    }

                    _automationService.Update(BuildUpdateRequest(definition.Id, form.Request));
                    RefreshData(UiLocalization.Text("AutomationsForm.Status.Updated", "Automation state updated."));
                }
            }
            finally
            {
                UpdateActionButtons();
                UpdateContextMenuItems();
            }
        }

        private static AutomationUpdateRequest BuildUpdateRequest(string automationId, AutomationCreationRequest request)
        {
            if (request == null)
            {
                return null;
            }

            return new AutomationUpdateRequest
            {
                Id = automationId,
                Name = request.Name,
                Description = request.Description,
                Kind = request.Kind,
                Prompt = request.Prompt,
                InputItems = request.InputItems,
                Schedule = request.Schedule,
                Context = request.Context,
                ModelId = request.ModelId,
                ReasoningEffort = request.ReasoningEffort,
                ApprovalMode = request.ApprovalMode,
                AllowNoFindingsAutoArchive = request.AllowNoFindingsAutoArchive
            };
        }

        private async void RunNowButton_Click(object sender, EventArgs e)
        {
            await RunSelectedAutomationAsync();
        }

        private async void StopRunButton_Click(object sender, EventArgs e)
        {
            await StopSelectedRunAsync();
        }

        private void DeleteAutomationMenuItem_Click(object sender, EventArgs e)
        {
            DeleteSelectedAutomation();
        }

        private void ToggleAutomationEnabledMenuItem_Click(object sender, EventArgs e)
        {
            ToggleSelectedAutomationEnabled();
        }

        private void DetailsButton_Click(object sender, EventArgs e)
        {
            OpenSelectedRunDetails();
        }

        private async void OpenThreadButton_Click(object sender, EventArgs e)
        {
            await OpenSelectedRunThreadAsync();
        }

        private async void DeleteRunMenuItem_Click(object sender, EventArgs e)
        {
            await DeleteSelectedRunRecordAsync();
        }

        private void RunListView_OpenDetails(object sender, EventArgs e)
        {
            OpenSelectedRunDetails();
        }

        private void RunListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            OpenSelectedRunDetails();
            e.Handled = true;
        }

        private void ListView_SelectItemOnRightClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var listView = sender as ListView;
            if (listView == null)
            {
                return;
            }

            var item = listView.GetItemAt(e.X, e.Y);
            if (item == null)
            {
                return;
            }

            listView.SelectedItems.Clear();
            item.Selected = true;
            item.Focused = true;
            listView.Focus();
            UpdateActionButtons();
            UpdateContextMenuItems();
        }

        private void AutomationContextMenu_Popup(object sender, EventArgs e)
        {
            UpdateContextMenuItems();
        }

        private void RunContextMenu_Popup(object sender, EventArgs e)
        {
            UpdateContextMenuItems();
        }

        private async Task RunSelectedAutomationAsync()
        {
            var definition = GetSelectedAutomation();
            if (definition == null)
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.SelectAutomation", "Select an automation first.");
                return;
            }

            if (_automationRunner == null)
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.RunnerUnavailable", "Automation runner is unavailable.");
                return;
            }

            var displayName = DisplayAutomationName(definition);
            _statusLabel.Text = string.Format(
                UiLocalization.Text("AutomationsForm.Status.StartingRun", "Starting automation: {0}"),
                displayName);
            _runNowButton.Enabled = false;
            try
            {
                await _automationRunner.StartAsync(new AutomationRunRequest
                {
                    AutomationId = definition.Id,
                    TriggerReason = AutomationTriggerReason.Manual,
                    ScheduledFor = DateTime.Now
                });
                RefreshData();
                _tabs.SelectedTab = _tabs.TabPages[1];
                _statusLabel.Text = string.Format(
                    UiLocalization.Text("AutomationsForm.Status.RunStarted", "Automation started: {0}. Check run results for progress."),
                    displayName);
            }
            catch (Exception ex)
            {
                RefreshData();
                var message = string.Format(
                    UiLocalization.Text("AutomationsForm.Status.RunFailed", "Automation failed to start: {0}"),
                    ex.Message);
                _statusLabel.Text = message;
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _runNowButton.Enabled = true;
            }
        }

        private async Task StopSelectedRunAsync()
        {
            var run = GetSelectedRun();
            if (!CanStopRun(run))
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.NoStoppableRun", "Select a running automation run first.");
                return;
            }

            _stopRunButton.Enabled = false;
            try
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.RequestingStop", "Requesting automation stop...");
                await _runActionCallbacks.StopRunAsync(run);
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.StopRequested", "Automation stop requested.");
            }
            catch (Exception ex)
            {
                var message = string.Format(
                    UiLocalization.Text("AutomationsForm.Status.StopFailed", "Failed to stop automation: {0}"),
                    ex.Message);
                _statusLabel.Text = message;
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UpdateActionButtons();
            }
        }

        private void OpenSelectedRunDetails()
        {
            var run = GetSelectedResultRun();
            if (run == null)
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.SelectRun", "Select an automation run first.");
                return;
            }

            var form = new AutomationRunDetailsForm(
                run,
                ResolveAutomationName(BuildAutomationNameMap(_store.Load() ?? new AutomationStateSnapshot()), run),
                _runActionCallbacks,
                _runActivityCallbacks);
            form.FormClosed += (sender, e) => form.Dispose();
            form.Show(this);
            form.Activate();
        }

        private async Task OpenSelectedRunThreadAsync()
        {
            var run = GetSelectedResultRun();
            if (!CanOpenThread(run))
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.NoThreadToOpen", "Select a run with a thread first.");
                return;
            }

            await _runActionCallbacks.OpenThreadAsync(run).ConfigureAwait(true);
        }

        private void ToggleSelectedAutomationEnabled()
        {
            var definition = GetSelectedAutomation();
            if (definition == null)
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.SelectAutomation", "Select an automation first.");
                return;
            }

            if (_automationService == null)
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.ServiceUnavailable", "Automation service is unavailable.");
                return;
            }

            var displayName = DisplayAutomationName(definition);
            try
            {
                if (definition.Enabled)
                {
                    _automationService.Disable(definition.Id);
                    RefreshData(string.Format(
                        UiLocalization.Text("AutomationsForm.Status.AutomationDisabled", "Automation disabled: {0}"),
                        displayName));
                }
                else
                {
                    _automationService.Enable(definition.Id);
                    RefreshData(string.Format(
                        UiLocalization.Text("AutomationsForm.Status.AutomationEnabled", "Automation enabled: {0}"),
                        displayName));
                }
            }
            catch (Exception ex)
            {
                var message = string.Format(
                    UiLocalization.Text("AutomationsForm.Status.ToggleAutomationEnabledFailed", "Failed to update automation enabled state: {0}"),
                    ex.Message);
                _statusLabel.Text = message;
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DeleteSelectedAutomation()
        {
            var definition = GetSelectedAutomation();
            if (definition == null)
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.SelectAutomation", "Select an automation first.");
                return;
            }

            if (_automationService == null)
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.ServiceUnavailable", "Automation service is unavailable.");
                return;
            }

            if (!ShouldDeleteAutomation(definition))
            {
                return;
            }

            var displayName = DisplayAutomationName(definition);
            try
            {
                _automationService.Delete(definition.Id);
                RefreshData(string.Format(
                    UiLocalization.Text("AutomationsForm.Status.AutomationDeleted", "Automation deleted: {0}"),
                    displayName));
            }
            catch (Exception ex)
            {
                var message = string.Format(
                    UiLocalization.Text("AutomationsForm.Status.DeleteAutomationFailed", "Failed to delete automation: {0}"),
                    ex.Message);
                _statusLabel.Text = message;
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task DeleteSelectedRunRecordAsync()
        {
            var run = GetSelectedResultRun();
            if (run == null)
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.SelectRun", "Select an automation run first.");
                return;
            }

            if (_automationService == null)
            {
                _statusLabel.Text = UiLocalization.Text("AutomationsForm.Status.ServiceUnavailable", "Automation service is unavailable.");
                return;
            }

            var confirmation = ConfirmRunDeletion(run);
            if (confirmation == null || !confirmation.ShouldDelete)
            {
                return;
            }

            try
            {
                if (confirmation.DeleteLocalHistory)
                {
                    if (_runActionCallbacks == null || _runActionCallbacks.DeleteLocalHistoryAsync == null)
                    {
                        throw new InvalidOperationException(UiLocalization.Text(
                            "AutomationsForm.Status.DeleteRunLocalHistoryUnavailable",
                            "Local history deletion is unavailable."));
                    }

                    await _runActionCallbacks.DeleteLocalHistoryAsync(run).ConfigureAwait(true);
                }

                _automationService.DeleteRun(run.Id);
                RefreshData(UiLocalization.Text("AutomationsForm.Status.RunDeleted", "Automation run result deleted."));
            }
            catch (Exception ex)
            {
                var message = string.Format(
                    UiLocalization.Text("AutomationsForm.Status.DeleteRunFailed", "Failed to delete automation run result: {0}"),
                    ex.Message);
                _statusLabel.Text = message;
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool ShouldDeleteAutomation(AutomationDefinitionSnapshot definition)
        {
            if (ConfirmDeleteAutomation != null)
            {
                return ConfirmDeleteAutomation(definition);
            }

            var message = string.Format(
                UiLocalization.Text("AutomationsForm.Confirm.DeleteAutomation.Message", "Delete automation \"{0}\"? This also deletes its run results."),
                DisplayAutomationName(definition));
            return MessageBox.Show(
                this,
                message,
                UiLocalization.Text("AutomationsForm.Confirm.DeleteAutomation.Title", "Delete automation"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private AutomationRunDeleteConfirmation ConfirmRunDeletion(AutomationRunSnapshot run)
        {
            if (ConfirmDeleteRunOptions != null)
            {
                return ConfirmDeleteRunOptions(run) ?? new AutomationRunDeleteConfirmation(false, false);
            }

            if (ConfirmDeleteRun != null)
            {
                return new AutomationRunDeleteConfirmation(ConfirmDeleteRun(run), false);
            }

            using (var form = new LocalSessionDeleteConfirmationForm(
                UiLocalization.Text("AutomationsForm.Confirm.DeleteRun.Message", "Delete this automation run result? This removes it from local automation results."),
                CanDeleteRunLocalHistory(run),
                false))
            {
                return form.ShowDialog(this) == DialogResult.OK
                    ? new AutomationRunDeleteConfirmation(true, form.DeleteLocalHistory)
                    : new AutomationRunDeleteConfirmation(false, false);
            }
        }

        private bool CanDeleteRunLocalHistory(AutomationRunSnapshot run)
        {
            return run != null &&
                   !string.IsNullOrWhiteSpace(run.ThreadId) &&
                   _runActionCallbacks != null &&
                   _runActionCallbacks.DeleteLocalHistoryAsync != null;
        }

        private void RefreshData()
        {
            RefreshData(UiLocalization.Text("AutomationsForm.Status.Ready", "Automations loaded."));
        }

        private void RefreshData(string statusText)
        {
            var snapshot = _store.Load() ?? new AutomationStateSnapshot();
            BindAutomations(snapshot);
            BindRuns(snapshot);
            _statusLabel.Text = statusText;
            UpdateActionButtons();
        }

        private void BindAutomations(AutomationStateSnapshot snapshot)
        {
            _automationsListView.Items.Clear();
            foreach (var definition in (snapshot.Definitions ?? new List<AutomationDefinitionSnapshot>()).Where(item => item != null))
            {
                _automationsListView.Items.Add(new ListViewItem(new[]
                {
                    definition.Name ?? definition.Id,
                    AutomationDisplayText.Kind(definition.Kind),
                    definition.Enabled ? UiLocalization.Text("AutomationsForm.Value.Enabled", "Enabled") : UiLocalization.Text("AutomationsForm.Value.Disabled", "Disabled"),
                    FormatDate(definition.NextRunAt)
                }) { Tag = definition });
            }
        }

        private void BindRuns(AutomationStateSnapshot snapshot)
        {
            _runsListView.Items.Clear();
            var automationNames = BuildAutomationNameMap(snapshot);
            foreach (var run in (snapshot.Runs ?? new List<AutomationRunSnapshot>())
                .Where(item => item != null)
                .OrderByDescending(item => item.StartedAt ?? item.ScheduledFor ?? DateTime.MinValue))
            {
                _runsListView.Items.Add(new ListViewItem(new[]
                {
                    ResolveAutomationName(automationNames, run),
                    AutomationDisplayText.RunStatus(run.Status),
                    FormatDate(run.StartedAt)
                }) { Tag = run });
            }
        }

        private AutomationDefinitionSnapshot GetSelectedAutomation()
        {
            if (_automationsListView.SelectedItems.Count == 0)
            {
                return null;
            }

            return _automationsListView.SelectedItems[0].Tag as AutomationDefinitionSnapshot;
        }

        private AutomationRunSnapshot GetSelectedRun()
        {
            if (_runsListView.SelectedItems.Count == 0)
            {
                return null;
            }

            return _runsListView.SelectedItems[0].Tag as AutomationRunSnapshot;
        }

        private AutomationRunSnapshot GetSelectedResultRun()
        {
            return GetSelectedRun();
        }

        private void UpdateActionButtons()
        {
            if (_tabs.TabPages.Count < 2)
            {
                return;
            }

            var automationsTabSelected = _tabs.SelectedIndex == 0;
            var runHistoryTabSelected = _tabs.SelectedIndex == 1;
            var selectedAutomation = GetSelectedAutomation();
            var selectedResultRun = GetSelectedResultRun();
            _newButton.Visible = automationsTabSelected;
            _newButton.Enabled = automationsTabSelected && _automationService != null &&
                (_targetCreationContext == null || !string.IsNullOrWhiteSpace(_targetCreationContext.WorkspacePath));
            _assignPendingButton.Visible = automationsTabSelected && AssignPendingAutomations != null;
            _assignPendingButton.Enabled = automationsTabSelected && AssignPendingAutomations != null;
            _runNowButton.Visible = automationsTabSelected;
            _runNowButton.Enabled = automationsTabSelected && selectedAutomation != null;
            _detailsButton.Visible = runHistoryTabSelected;
            _detailsButton.Enabled = selectedResultRun != null;
            _openThreadButton.Visible = runHistoryTabSelected;
            _openThreadButton.Enabled = CanOpenThread(selectedResultRun);
            _stopRunButton.Visible = runHistoryTabSelected;
            _stopRunButton.Enabled = runHistoryTabSelected && CanStopRun(GetSelectedRun());
            UpdateContextMenuItems();
        }

        private void UpdateContextMenuItems()
        {
            var selectedAutomation = GetSelectedAutomation();
            var selectedResultRun = GetSelectedResultRun();
            _editAutomationMenuItem.Enabled = _automationService != null && selectedAutomation != null;
            _toggleAutomationEnabledMenuItem.Enabled = _automationService != null && selectedAutomation != null;
            _toggleAutomationEnabledMenuItem.Text = selectedAutomation == null || selectedAutomation.Enabled
                ? UiLocalization.Text("AutomationsForm.Menu.DisableAutomation", "Disable(&B)")
                : UiLocalization.Text("AutomationsForm.Menu.EnableAutomation", "Enable(&N)");
            _deleteAutomationMenuItem.Enabled = _automationService != null && selectedAutomation != null;
            _deleteRunMenuItem.Enabled = _automationService != null && selectedResultRun != null;
        }

        private bool CanStopRun(AutomationRunSnapshot run)
        {
            return run != null &&
                   _runActionCallbacks != null &&
                   _runActionCallbacks.StopRunAsync != null &&
                   _runActionCallbacks.CanStopRun != null &&
                   _runActionCallbacks.CanStopRun(run);
        }

        private bool CanOpenThread(AutomationRunSnapshot run)
        {
            return run != null &&
                   _runActionCallbacks != null &&
                   _runActionCallbacks.OpenThreadAsync != null &&
                   _runActionCallbacks.CanOpenThread != null &&
                   _runActionCallbacks.CanOpenThread(run);
        }

        private static TabPage CreateTab(string text, Control content)
        {
            var page = new TabPage(text) { UseVisualStyleBackColor = true };
            page.Controls.Add(content);
            return page;
        }

        private static ListView CreateListView(string accessibleName)
        {
            var listView = new ListView
            {
                AccessibleName = accessibleName,
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details
            };
            UiTheme.ApplyListView(listView);
            return listView;
        }

        private static void AddDefaultColumns(ListView listView)
        {
            listView.Columns.Add(UiLocalization.Text("AutomationsForm.Column.Name", "Name"), 280);
            listView.Columns.Add(UiLocalization.Text("AutomationsForm.Column.Status", "Status"), 140);
            listView.Columns.Add(UiLocalization.Text("AutomationsForm.Column.Time", "Time"), 180);
        }

        private static void AddAutomationColumns(ListView listView)
        {
            listView.Columns.Add(UiLocalization.Text("AutomationsForm.Column.Name", "Name"), 280);
            listView.Columns.Add(UiLocalization.Text("AutomationsForm.Column.Type", "Type"), 140);
            listView.Columns.Add(UiLocalization.Text("AutomationsForm.Column.Status", "Status"), 120);
            listView.Columns.Add(UiLocalization.Text("AutomationsForm.Column.Time", "Time"), 180);
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex, UiGlyph glyph = UiGlyph.Info)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                TabIndex = tabIndex,
                UseMnemonic = true
            };
            UiTheme.ApplyDialogButton(button, glyph);
            return button;
        }

        private static string FormatDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty;
        }

        private static Dictionary<string, string> BuildAutomationNameMap(AutomationStateSnapshot snapshot)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in (snapshot.Definitions ?? new List<AutomationDefinitionSnapshot>()).Where(item => item != null))
            {
                if (string.IsNullOrWhiteSpace(definition.Id))
                {
                    continue;
                }

                result[definition.Id] = DisplayAutomationName(definition);
            }

            return result;
        }

        private static string ResolveAutomationName(Dictionary<string, string> automationNames, AutomationRunSnapshot run)
        {
            if (run == null)
            {
                return string.Empty;
            }

            string name;
            if (!string.IsNullOrWhiteSpace(run.AutomationId) &&
                automationNames.TryGetValue(run.AutomationId, out name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return string.IsNullOrWhiteSpace(run.AutomationId) ? run.Id : run.AutomationId;
        }

        private static string DisplayAutomationName(AutomationDefinitionSnapshot definition)
        {
            if (definition == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(definition.Name) ? definition.Id : definition.Name.Trim();
        }
    }
}
