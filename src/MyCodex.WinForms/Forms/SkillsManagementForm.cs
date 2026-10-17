using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Skills;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class SkillsManagementForm : Form
    {
        private readonly IAppServerConnectionClient _connectionClient;
        private readonly IAppServerSkillClient _skillClient;
        private readonly IAppServerPluginClient _pluginClient;
        private readonly IAppServerFileSystemClient _fileSystemClient;
        private readonly string _workspacePath;
        private readonly OfficialSkillFileOperations _skillFileOperations;
        private readonly ListView _skillListView;
        private readonly ComboBox _filterComboBox;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly Button _newButton;
        private readonly Button _editButton;
        private readonly Button _enableButton;
        private readonly Button _disableButton;
        private readonly Button _uninstallButton;
        private readonly Button _openFolderButton;
        private readonly Button _errorsButton;
        private readonly Button _closeButton;
        private readonly ContextMenu _skillContextMenu;
        private readonly MenuItem _editMenuItem;
        private readonly MenuItem _enableMenuItem;
        private readonly MenuItem _disableMenuItem;
        private readonly MenuItem _uninstallMenuItem;
        private readonly MenuItem _openFolderMenuItem;

        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private bool _isBusy;
        private string _preferredSkillPath;
        private AppServerSkillsSnapshot _currentSnapshot;
        private List<AppServerPluginSummary> _skillSourcePlugins;

        public SkillsManagementForm(IAppServerClient appServerClient, string workspacePath)
            : this(
                appServerClient,
                appServerClient,
                appServerClient,
                appServerClient,
                workspacePath)
        {
        }

        internal SkillsManagementForm(
            IAppServerConnectionClient connectionClient,
            IAppServerSkillClient skillClient,
            IAppServerPluginClient pluginClient,
            IAppServerFileSystemClient fileSystemClient,
            string workspacePath)
        {
            _connectionClient = connectionClient ?? throw new ArgumentNullException(nameof(connectionClient));
            _skillClient = skillClient ?? throw new ArgumentNullException(nameof(skillClient));
            _pluginClient = pluginClient ?? throw new ArgumentNullException(nameof(pluginClient));
            _fileSystemClient = fileSystemClient ?? throw new ArgumentNullException(nameof(fileSystemClient));
            _workspacePath = workspacePath ?? throw new ArgumentNullException(nameof(workspacePath));
            _skillSourcePlugins = new List<AppServerPluginSummary>();
            _skillFileOperations = new OfficialSkillFileOperations(_fileSystemClient, _workspacePath);

            Text = UiLocalization.Text("SkillsManagementForm.Title", "Skills Management");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(980, 640);
            Size = new Size(1120, 760);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _skillListView = CreateSkillListView();
            _filterComboBox = CreateFilterComboBox();
            _filterComboBox.Items.Add(UiLocalization.Text("SkillsManagementForm.Filter.All", "All"));
            _filterComboBox.Items.Add(UiLocalization.Text("SkillsManagementForm.Filter.Enabled", "Enabled"));
            _filterComboBox.Items.Add(UiLocalization.Text("SkillsManagementForm.Filter.Disabled", "Disabled"));
            _filterComboBox.SelectedIndex = 0;
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 8, 0, 0),
                TabStop = false
            };
            _refreshButton = CreateButton(
                UiLocalization.Text("SkillsManagementForm.Button.Refresh", "Refresh(&R)"),
                UiLocalization.Text("SkillsManagementForm.Accessible.Refresh", "Refresh"),
                0);
            _newButton = CreateButton(
                UiLocalization.Text("SkillsManagementForm.Button.New", "New(&N)..."),
                UiLocalization.Text("SkillsManagementForm.Accessible.New", "New"),
                1);
            _editButton = CreateButton(
                UiLocalization.Text("SkillsManagementForm.Button.Edit", "Edit(&T)..."),
                UiLocalization.Text("SkillsManagementForm.Accessible.Edit", "Edit"),
                2);
            _enableButton = CreateButton(
                UiLocalization.Text("SkillsManagementForm.Button.Enable", "Enable(&E)"),
                UiLocalization.Text("SkillsManagementForm.Accessible.Enable", "Enable"),
                3);
            _disableButton = CreateButton(
                UiLocalization.Text("SkillsManagementForm.Button.Disable", "Disable(&D)"),
                UiLocalization.Text("SkillsManagementForm.Accessible.Disable", "Disable"),
                4);
            _uninstallButton = CreateButton(
                UiLocalization.Text("SkillsManagementForm.Button.Uninstall", "Uninstall(&U)"),
                UiLocalization.Text("SkillsManagementForm.Accessible.Uninstall", "Uninstall"),
                5);
            _openFolderButton = CreateButton(
                UiLocalization.Text("SkillsManagementForm.Button.OpenFolder", "Open Folder(&O)"),
                UiLocalization.Text("SkillsManagementForm.Accessible.OpenFolder", "Open Folder"),
                6);
            _errorsButton = CreateButton(
                UiLocalization.Text("SkillsManagementForm.Button.Errors", "Errors(&W)..."),
                UiLocalization.Text("SkillsManagementForm.Accessible.Errors", "Errors"),
                7);
            _closeButton = CreateButton(
                UiLocalization.Text("SkillsManagementForm.Button.Close", "Close(&C)"),
                UiLocalization.Text("SkillsManagementForm.Accessible.Close", "Close"),
                8);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_newButton, UiGlyph.NewSession);
            UiTheme.ApplyButton(_editButton, UiGlyph.FileChange);
            UiTheme.ApplyButton(_enableButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_disableButton, UiGlyph.Cancel);
            UiTheme.ApplyButton(_uninstallButton, UiGlyph.Delete);
            UiTheme.ApplyButton(_openFolderButton, UiGlyph.Workspace);
            UiTheme.ApplyButton(_errorsButton, UiGlyph.Error);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _skillContextMenu = new ContextMenu();
            _editMenuItem = new MenuItem(UiLocalization.Text("SkillsManagementForm.Menu.Edit", "Edit(&T)..."));
            _enableMenuItem = new MenuItem(UiLocalization.Text("SkillsManagementForm.Menu.Enable", "Enable(&E)"));
            _disableMenuItem = new MenuItem(UiLocalization.Text("SkillsManagementForm.Menu.Disable", "Disable(&D)"));
            _uninstallMenuItem = new MenuItem(UiLocalization.Text("SkillsManagementForm.Menu.Uninstall", "Uninstall(&U)"));
            _openFolderMenuItem = new MenuItem(UiLocalization.Text("SkillsManagementForm.Menu.OpenFolder", "Open Folder(&O)"));
            _skillContextMenu.MenuItems.Add(_editMenuItem);
            _skillContextMenu.MenuItems.Add("-");
            _skillContextMenu.MenuItems.Add(_enableMenuItem);
            _skillContextMenu.MenuItems.Add(_disableMenuItem);
            _skillContextMenu.MenuItems.Add("-");
            _skillContextMenu.MenuItems.Add(_uninstallMenuItem);
            _skillContextMenu.MenuItems.Add(_openFolderMenuItem);

            _skillListView.ContextMenu = _skillContextMenu;
            _filterComboBox.SelectedIndexChanged += FilterComboBox_SelectedIndexChanged;
            _skillListView.SelectedIndexChanged += SkillListView_SelectedIndexChanged;
            _skillListView.KeyDown += SkillListView_KeyDown;
            _skillListView.MouseDown += SkillListView_MouseDown;
            _skillContextMenu.Popup += SkillContextMenu_Popup;
            _refreshButton.Click += RefreshButton_Click;
            _newButton.Click += NewButton_Click;
            _editButton.Click += EditButton_Click;
            _enableButton.Click += EnableButton_Click;
            _disableButton.Click += DisableButton_Click;
            _uninstallButton.Click += UninstallButton_Click;
            _openFolderButton.Click += OpenFolderButton_Click;
            _errorsButton.Click += ErrorsButton_Click;
            _closeButton.Click += CloseButton_Click;
            _editMenuItem.Click += EditButton_Click;
            _enableMenuItem.Click += EnableButton_Click;
            _disableMenuItem.Click += DisableButton_Click;
            _uninstallMenuItem.Click += UninstallButton_Click;
            _openFolderMenuItem.Click += OpenFolderButton_Click;
            Load += OnLoad;
            Shown += OnShown;
            FormClosed += OnFormClosed;
            KeyDown += SkillsManagementForm_KeyDown;
            _connectionClient.EventReceived += AppServerClient_EventReceived;
            BuildLayout();
            CancelButton = _closeButton;
            SetStatus(UiLocalization.Text("SkillsManagementForm.Status.Preparing", "Preparing to read skill list."));
            RefreshActionButtons();
        }

        private void OnLoad(object sender, EventArgs e)
        {
            _skillListView.Select();
        }

        private async void OnShown(object sender, EventArgs e)
        {
            await LoadSkillsAsync();
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            _connectionClient.EventReceived -= AppServerClient_EventReceived;
            _lifetimeCancellation.Cancel();
        }

        private async void SkillsManagementForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                e.SuppressKeyPress = true;
                await LoadSkillsAsync();
            }
        }

        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            await LoadSkillsAsync();
        }

        private async void NewButton_Click(object sender, EventArgs e)
        {
            await ShowSkillEditFormAsync(null);
        }

        private async void EditButton_Click(object sender, EventArgs e)
        {
            await ShowSkillEditFormAsync(GetSelectedSkill());
        }

        private async void EnableButton_Click(object sender, EventArgs e)
        {
            await SetSelectedSkillEnabledAsync(true);
        }

        private async void DisableButton_Click(object sender, EventArgs e)
        {
            await SetSelectedSkillEnabledAsync(false);
        }

        private void OpenFolderButton_Click(object sender, EventArgs e)
        {
            OpenSelectedSkillFolder();
        }

        private async void UninstallButton_Click(object sender, EventArgs e)
        {
            await UninstallSelectedSkillAsync();
        }

        private void ErrorsButton_Click(object sender, EventArgs e)
        {
            ShowErrorsDialog();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void FilterComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            ApplySkillFilter();
            UpdateStatusForCurrentFilter();
        }

        private void SkillListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshActionButtons();
        }

        private void SkillListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || _isBusy)
            {
                return;
            }

            e.SuppressKeyPress = true;
            ShowSelectedSkillDetails();
        }

        private void SkillListView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var hit = _skillListView.HitTest(e.Location);
            if (hit == null || hit.Item == null)
            {
                return;
            }

            hit.Item.Selected = true;
            hit.Item.Focused = true;
        }

        private void SkillContextMenu_Popup(object sender, EventArgs e)
        {
            RefreshContextMenuItems();
        }

        private void AppServerClient_EventReceived(object sender, AppServerEventArgs e)
        {
            if (e == null || !string.Equals(e.EventType, "skills_changed", StringComparison.OrdinalIgnoreCase) || IsDisposed)
            {
                return;
            }

            if (InvokeRequired || !IsHandleCreated)
            {
                this.BeginInvokeSafe(() => AppServerClient_EventReceived(sender, e));
                return;
            }

            SetStatus(UiLocalization.Text("SkillsManagementForm.Status.SkillsChanged", "Detected skill file changes. Press Refresh to rescan."));
        }

        private async System.Threading.Tasks.Task LoadSkillsAsync()
        {
            if (_isBusy)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(
                    UiLocalization.Text("SkillsManagementForm.Action.LoadSkillList", "Load skill list")))
            {
                return;
            }

            try
            {
                SetBusy(true);
                await LoadSkillsCoreAsync();
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("SkillsManagementForm.Status.LoadFailed", "Failed to load skill list."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
                if (_skillListView.Items.Count > 0)
                {
                    this.BeginInvokeSafe(() => _skillListView.Focus());
                }
            }
        }

        private async System.Threading.Tasks.Task LoadSkillsCoreAsync()
        {
            SetStatus(UiLocalization.Text("SkillsManagementForm.Status.Loading", "Loading skill list..."));

            var snapshots = await _skillClient.ListSkillsAsync(new[] { _workspacePath }, true);
            var pluginSources = await SkillSourcePluginLoader.TryLoadAsync(
                _pluginClient,
                _connectionClient.ProtocolCapabilities);
            var snapshot = SelectSnapshotForWorkspace(snapshots, _workspacePath);
            BindSkills(snapshot ?? new AppServerSkillsSnapshot
            {
                WorkspacePath = _workspacePath,
                Skills = new List<AppServerSkillInfo>(),
                Errors = new List<AppServerSkillError>()
            }, pluginSources);

            UpdateStatusForCurrentFilter();
        }

        private async System.Threading.Tasks.Task UninstallSelectedSkillAsync()
        {
            var skill = GetSelectedSkill();
            if (skill == null || _isBusy)
            {
                return;
            }

            string reason;
            if (!CanUninstallSkill(skill, out reason))
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    reason = UiLocalization.Text(
                        "SkillsManagementForm.Message.UninstallNotAllowed",
                        "Only user skills can be uninstalled here. Plugin and system skills are read-only.");
                }

                SetStatus(reason);
                MessageBox.Show(this, reason, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var displayName = GetSkillDisplayName(skill);
            var confirmation = string.Format(
                UiLocalization.Text(
                    "SkillsManagementForm.Message.ConfirmUninstallSkill.Template",
                    "Uninstall {0}?\r\n\r\nThis deletes the user skill folder:\r\n{1}"),
                displayName,
                skill.Path);
            if (MessageBox.Show(
                    this,
                    confirmation,
                    AppConstants.AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                SetBusy(true);
                SetStatus(string.Format(
                    UiLocalization.Text("SkillsManagementForm.Status.UninstallingSkill", "Uninstalling skill {0}..."),
                    displayName));

                await _skillFileOperations.DeleteAsync(skill);
                _preferredSkillPath = null;
                await LoadSkillsCoreAsync();
                SetStatus(string.Format(
                    UiLocalization.Text("SkillsManagementForm.Status.SkillUninstalled", "{0} has been uninstalled."),
                    displayName));
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("SkillsManagementForm.Status.SkillUninstallFailed", "Failed to uninstall skill."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async System.Threading.Tasks.Task SetSelectedSkillEnabledAsync(bool enabled)
        {
            var skill = GetSelectedSkill();
            if (skill == null || _isBusy || skill.Enabled == enabled)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(
                    enabled
                        ? UiLocalization.Text("SkillsManagementForm.Action.EnableSkill", "Enable skill")
                        : UiLocalization.Text("SkillsManagementForm.Action.DisableSkill", "Disable skill")))
            {
                return;
            }

            try
            {
                SetBusy(true);
                SetStatus(string.Format(
                    enabled
                        ? UiLocalization.Text("SkillsManagementForm.Status.Enabling", "Enabling skill {0}...")
                        : UiLocalization.Text("SkillsManagementForm.Status.Disabling", "Disabling skill {0}..."),
                    GetSkillDisplayName(skill)));

                var effectiveEnabled = await _skillClient.WriteSkillEnabledAsync(skill.Path, enabled);
                _preferredSkillPath = skill.Path;
                skill.Enabled = effectiveEnabled;
            }
            catch (Exception ex)
            {
                SetStatus(enabled
                    ? UiLocalization.Text("SkillsManagementForm.Status.EnableFailed", "Failed to enable skill.")
                    : UiLocalization.Text("SkillsManagementForm.Status.DisableFailed", "Failed to disable skill."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                SetBusy(false);
            }

            await LoadSkillsAsync();
            SetStatus(string.Format(
                skill.Enabled
                    ? UiLocalization.Text("SkillsManagementForm.Status.EnabledTemplate", "{0} has been enabled.")
                    : UiLocalization.Text("SkillsManagementForm.Status.DisabledTemplate", "{0} has been disabled."),
                GetSkillDisplayName(skill)));
        }

        private void OpenSelectedSkillFolder()
        {
            SetStatus(UiLocalization.Text(
                "SkillsManagementForm.Message.OpenFolderUnavailable",
                "Skill folders are managed through the selected App Server and cannot be opened directly."));
        }

        private void ShowErrorsDialog()
        {
            if (_currentSnapshot == null || _currentSnapshot.Errors == null || _currentSnapshot.Errors.Count == 0)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("SkillsManagementForm.Message.NoScanErrors", "There are no skill scan errors currently."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using (var form = new SkillErrorsForm(_currentSnapshot.Errors))
            {
                form.ShowDialog(this);
            }
        }

        private async System.Threading.Tasks.Task ShowSkillEditFormAsync(AppServerSkillInfo skill)
        {
            if (_isBusy)
            {
                return;
            }

            var isNew = skill == null;
            if (!isNew)
            {
                string reason;
                if (!CanEditSkill(skill, out reason))
                {
                    MessageBox.Show(
                        this,
                        string.Format(
                            UiLocalization.Text("SkillsManagementForm.Message.EditNotAllowed.Template", "This skill cannot be edited here.\r\n\r\n{0}"),
                            reason),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    SetStatus(UiLocalization.Text("SkillsManagementForm.Status.EditNotAllowed", "The selected skill is read-only."));
                    return;
                }
            }

            string savedSkillPath;
            string savedSkillDisplayName;
            try
            {
                SetBusy(true);
                var locations = _skillFileOperations.GetSaveLocations();
                var model = isNew
                    ? SkillFileEditor.CreateNewModel(locations)
                    : await _skillFileOperations.LoadForEditAsync(skill);
                using (var form = new SkillEditForm(
                    _workspacePath,
                    isNew,
                    model,
                    locations,
                    (editModel, newSkill) => _skillFileOperations.Validate(editModel, newSkill),
                    (editModel, newSkill) => _skillFileOperations.BuildTargetPath(editModel, newSkill),
                    (editModel, newSkill) => _skillFileOperations.SaveAsync(editModel, newSkill),
                    false))
                {
                    if (form.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    savedSkillPath = form.SavedSkillPath;
                    savedSkillDisplayName = form.SavedSkillDisplayName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                SetBusy(false);
            }

            _preferredSkillPath = savedSkillPath;
            await LoadSkillsAsync();
            SetStatus(string.Format(
                isNew
                    ? UiLocalization.Text("SkillsManagementForm.Status.SkillCreated.Template", "{0} has been created.")
                    : UiLocalization.Text("SkillsManagementForm.Status.SkillUpdated.Template", "{0} has been updated."),
                string.IsNullOrWhiteSpace(savedSkillDisplayName)
                    ? UiLocalization.Text("SkillsManagementForm.Value.UnnamedSkill", "(Unnamed Skill)")
                    : savedSkillDisplayName));
        }

        private void BindSkills(AppServerSkillsSnapshot snapshot, List<AppServerPluginSummary> pluginSources)
        {
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            _currentSnapshot = snapshot ?? new AppServerSkillsSnapshot
            {
                WorkspacePath = _workspacePath,
                Skills = new List<AppServerSkillInfo>(),
                Errors = new List<AppServerSkillError>()
            };
            _skillSourcePlugins = pluginSources ?? new List<AppServerPluginSummary>();
            _skillFileOperations.RefreshObservations(_currentSnapshot.Skills);

            ApplySkillFilter();
        }

        private void ApplySkillFilter()
        {
            var selectedSkillPath = !string.IsNullOrWhiteSpace(_preferredSkillPath)
                ? _preferredSkillPath
                : GetSelectedSkillPath();
            var skills = GetVisibleSkills().ToList();

            _skillListView.BeginUpdate();
            try
            {
                _skillListView.Items.Clear();

                foreach (var skill in skills)
                {
                    var item = new ListViewItem(GetSkillDisplayName(skill));
                    item.SubItems.Add(skill.Enabled
                        ? UiLocalization.Text("SkillsManagementForm.Value.Enabled", "Enabled")
                        : UiLocalization.Text("SkillsManagementForm.Value.Disabled", "Disabled"));
                    item.SubItems.Add(ResolveSkillSource(skill).DisplayText);
                    item.SubItems.Add(GetSkillListSummary(skill));
                    item.Tag = skill;
                    _skillListView.Items.Add(item);
                }

                SelectSkillByPath(selectedSkillPath);
            }
            finally
            {
                _skillListView.EndUpdate();
            }

            _preferredSkillPath = null;
            RefreshActionButtons();
        }

        private IEnumerable<AppServerSkillInfo> GetVisibleSkills()
        {
            var filter = GetCurrentSkillFilter();
            IEnumerable<AppServerSkillInfo> query = _currentSnapshot == null || _currentSnapshot.Skills == null
                ? Enumerable.Empty<AppServerSkillInfo>()
                : _currentSnapshot.Skills.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path));

            switch (filter)
            {
                case SkillStatusFilter.Enabled:
                    query = query.Where(item => item.Enabled);
                    break;
                case SkillStatusFilter.Disabled:
                    query = query.Where(item => !item.Enabled);
                    break;
            }

            if (filter == SkillStatusFilter.All)
            {
                query = query
                    .OrderByDescending(item => item.Enabled)
                    .ThenBy(item => GetSkillDisplayName(item), StringComparer.CurrentCultureIgnoreCase);
            }
            else
            {
                query = query.OrderBy(item => GetSkillDisplayName(item), StringComparer.CurrentCultureIgnoreCase);
            }

            return query;
        }

        private SkillStatusFilter GetCurrentSkillFilter()
        {
            switch (_filterComboBox.SelectedIndex)
            {
                case 1:
                    return SkillStatusFilter.Enabled;
                case 2:
                    return SkillStatusFilter.Disabled;
                default:
                    return SkillStatusFilter.All;
            }
        }

        private void UpdateStatusForCurrentFilter()
        {
            var totalCount = _currentSnapshot == null || _currentSnapshot.Skills == null
                ? 0
                : _currentSnapshot.Skills.Count(item => item != null && !string.IsNullOrWhiteSpace(item.Path));
            var errorCount = _currentSnapshot == null || _currentSnapshot.Errors == null
                ? 0
                : _currentSnapshot.Errors.Count;

            if (totalCount == 0)
            {
                SetStatus(errorCount > 0
                    ? string.Format(
                        UiLocalization.Text("SkillsManagementForm.Status.NoSkillsWithErrors", "No available skills found, but there are {0} scan errors."),
                        errorCount)
                    : UiLocalization.Text("SkillsManagementForm.Status.NoSkills", "No skills found in the current workspace."));
                return;
            }

            if (_skillListView.Items.Count == 0)
            {
                SetStatus(UiLocalization.Text("SkillsManagementForm.Status.NoSkillsForFilter", "No skills match the current filter."));
                return;
            }

            if (errorCount > 0)
            {
                SetStatus(string.Format(
                    UiLocalization.Text("SkillsManagementForm.Status.LoadedWithErrors", "Loaded {0} skills. There are {1} scan errors. Select a skill and press Enter to open details."),
                    totalCount,
                    errorCount));
                return;
            }

            SetStatus(string.Format(
                UiLocalization.Text("SkillsManagementForm.Status.Loaded", "Loaded {0} skills. Select a skill and press Enter to open details."),
                totalCount));
        }

        private void ShowSelectedSkillDetails()
        {
            var skill = GetSelectedSkill();
            if (skill == null)
            {
                return;
            }

            using (var form = new SkillDetailsForm(
                string.Format(
                    UiLocalization.Text("SkillDetailsForm.Title.Template", "Skill Details - {0}"),
                    GetSkillDisplayName(skill)),
                string.Format(
                    UiLocalization.Text("SkillDetailsForm.Header.Template", "Details for {0}"),
                    GetSkillDisplayName(skill)),
                BuildSkillDetailText(skill)))
            {
                form.ShowDialog(this);
            }
        }

        private string BuildSkillDetailText(AppServerSkillInfo skill)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Format(
                UiLocalization.Text("SkillsManagementForm.Detail.Skill", "Skill: {0}"),
                GetSkillDisplayName(skill)));
            builder.AppendLine(string.Format(
                UiLocalization.Text("SkillsManagementForm.Detail.InternalName", "Internal Name: {0}"),
                skill.Name ?? UiLocalization.Text("SkillsManagementForm.Value.Unknown", "(Unknown)")));
            builder.AppendLine(string.Format(
                UiLocalization.Text("SkillsManagementForm.Detail.Enabled", "Enabled: {0}"),
                skill.Enabled
                    ? UiLocalization.Text("SkillsManagementForm.Value.Yes", "Yes")
                    : UiLocalization.Text("SkillsManagementForm.Value.No", "No")));
            builder.AppendLine(string.Format(
                UiLocalization.Text("SkillsManagementForm.Detail.Scope", "Scope: {0}"),
                DescribeSkillScope(skill.Scope)));

            var source = ResolveSkillSource(skill);
            builder.AppendLine(string.Format(
                UiLocalization.Text("SkillsManagementForm.Detail.Source", "Source: {0}"),
                source.DisplayText));
            if (source.Kind == SkillSourceKind.Plugin)
            {
                if (!string.IsNullOrWhiteSpace(source.PluginDisplayName))
                {
                    builder.AppendLine(string.Format(
                        UiLocalization.Text("SkillsManagementForm.Detail.SourcePlugin", "Source Plugin: {0}"),
                        source.PluginDisplayName));
                }

                if (!string.IsNullOrWhiteSpace(source.MarketplaceName))
                {
                    builder.AppendLine(string.Format(
                        UiLocalization.Text("SkillsManagementForm.Detail.SourceMarketplace", "Source Marketplace: {0}"),
                        source.MarketplaceName));
                }

                if (!string.IsNullOrWhiteSpace(source.SourcePath))
                {
                    builder.AppendLine(string.Format(
                        UiLocalization.Text("SkillsManagementForm.Detail.SourcePath", "Source Path: {0}"),
                        source.SourcePath));
                }
            }

            builder.AppendLine(string.Format(
                UiLocalization.Text("SkillsManagementForm.Detail.Path", "Path: {0}"),
                string.IsNullOrWhiteSpace(skill.Path)
                    ? UiLocalization.Text("SkillsManagementForm.Value.Unknown", "(Unknown)")
                    : skill.Path));

            string editReason;
            var canEdit = CanEditSkill(skill, out editReason);
            builder.AppendLine(string.Format(
                UiLocalization.Text("SkillsManagementForm.Detail.Editable", "Editable: {0}"),
                canEdit
                    ? UiLocalization.Text("SkillsManagementForm.Value.Editable", "Yes")
                    : UiLocalization.Text("SkillsManagementForm.Value.ReadOnly", "No")));
            if (!canEdit && !string.IsNullOrWhiteSpace(editReason))
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text("SkillsManagementForm.Detail.EditReason", "Edit note: {0}"),
                    editReason));
            }

            if (!string.IsNullOrWhiteSpace(skill.ShortDescription))
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text("SkillsManagementForm.Detail.Summary", "Summary: {0}"),
                    skill.ShortDescription));
            }

            if (!string.IsNullOrWhiteSpace(skill.Description))
            {
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("SkillsManagementForm.Detail.DescriptionTitle", "Description:"));
                builder.AppendLine(skill.Description.Trim());
            }

            if (!string.IsNullOrWhiteSpace(skill.DefaultPrompt))
            {
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("SkillsManagementForm.Detail.DefaultPromptTitle", "Default Prompt:"));
                builder.AppendLine(skill.DefaultPrompt.Trim());
            }

            if (skill.ToolDependencies != null && skill.ToolDependencies.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("SkillsManagementForm.Detail.ToolDependenciesTitle", "Tool Dependencies:"));
                foreach (var dependency in skill.ToolDependencies)
                {
                    builder.AppendLine("- " + BuildDependencyText(dependency));
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildDependencyText(AppServerSkillToolDependency dependency)
        {
            if (dependency == null)
            {
                return UiLocalization.Text("SkillsManagementForm.Value.Unknown", "(Unknown)");
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(dependency.Type))
            {
                parts.Add(dependency.Type.Trim());
            }

            if (!string.IsNullOrWhiteSpace(dependency.Value))
            {
                parts.Add(dependency.Value.Trim());
            }

            if (!string.IsNullOrWhiteSpace(dependency.Command))
            {
                parts.Add(UiLocalization.Text("SkillsManagementForm.Dependency.CommandPrefix", "Command: ") + dependency.Command.Trim());
            }

            if (!string.IsNullOrWhiteSpace(dependency.Transport))
            {
                parts.Add(UiLocalization.Text("SkillsManagementForm.Dependency.TransportPrefix", "Transport: ") + dependency.Transport.Trim());
            }

            if (!string.IsNullOrWhiteSpace(dependency.Url))
            {
                parts.Add(UiLocalization.Text("SkillsManagementForm.Dependency.UrlPrefix", "URL: ") + dependency.Url.Trim());
            }

            if (!string.IsNullOrWhiteSpace(dependency.Description))
            {
                parts.Add(dependency.Description.Trim());
            }

            return parts.Count == 0
                ? UiLocalization.Text("SkillsManagementForm.Value.Unknown", "(Unknown)")
                : string.Join(" | ", parts);
        }

        private void RefreshActionButtons()
        {
            var skill = GetSelectedSkill();
            var hasSelection = skill != null;

            _refreshButton.Enabled = !_isBusy;
            _filterComboBox.Enabled = !_isBusy;
            _newButton.Enabled = !_isBusy && CanUseSkillFileEditing();
            _editButton.Enabled = !_isBusy && hasSelection && CanEditSkill(skill);
            _enableButton.Enabled = !_isBusy && hasSelection && !skill.Enabled;
            _disableButton.Enabled = !_isBusy && hasSelection && skill.Enabled;
            _uninstallButton.Enabled = !_isBusy && hasSelection && CanUninstallSkill(skill);
            _openFolderButton.Enabled = false;
            _errorsButton.Enabled = !_isBusy && _currentSnapshot != null && _currentSnapshot.Errors != null && _currentSnapshot.Errors.Count > 0;
            _closeButton.Enabled = !_isBusy;
        }


        private void RefreshContextMenuItems()
        {
            var skill = GetSelectedSkill();
            var hasSelection = skill != null;

            _editMenuItem.Enabled = !_isBusy && hasSelection && CanEditSkill(skill);
            _enableMenuItem.Enabled = !_isBusy && hasSelection && !skill.Enabled;
            _disableMenuItem.Enabled = !_isBusy && hasSelection && skill.Enabled;
            _uninstallMenuItem.Enabled = !_isBusy && hasSelection && CanUninstallSkill(skill);
            _openFolderMenuItem.Enabled = false;
        }

        private void SetBusy(bool busy)
        {
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            _isBusy = busy;
            UseWaitCursor = busy;
            RefreshActionButtons();
            RefreshContextMenuItems();
        }

        private void SetStatus(string text)
        {
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            _statusLabel.Text = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
        }

        private async System.Threading.Tasks.Task<bool> EnsureAppServerAvailableForActionAsync(string actionName)
        {
            try
            {
                await _connectionClient.ConnectAsync();
                if (!AppServerProtocolFeatureSupport.IsSupported(
                        _connectionClient.ProtocolCapabilities,
                        AppServerProtocolFeature.Skills))
                {
                    var featureName = UiLocalization.Text("SkillsManagementForm.Feature.Skills", "Skills");
                    var message = string.Format(
                        UiLocalization.Text(
                            "ProtocolCapabilities.Message.FeatureNotSupported.Template",
                            "Current Codex runtime does not support {0}. Switch to a standard or newer runtime to use this feature."),
                        featureName);
                    SetStatus(message);
                    MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(
                    UiLocalization.Text("SkillsManagementForm.Status.ActionFailedDisconnected", "{0} failed because App Server is not connected."),
                    actionName));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private AppServerSkillInfo GetSelectedSkill()
        {
            return _skillListView.SelectedItems.Count == 0 ? null : _skillListView.SelectedItems[0].Tag as AppServerSkillInfo;
        }

        private bool CanEditSkill(AppServerSkillInfo skill)
        {
            string reason;
            return CanEditSkill(skill, out reason);
        }

        private bool CanEditSkill(AppServerSkillInfo skill, out string reason)
        {
            var source = ResolveSkillSource(skill);
            if (source.Kind != SkillSourceKind.User && source.Kind != SkillSourceKind.Workspace)
            {
                reason = UiLocalization.Text(
                    "SkillsManagementForm.Message.UninstallNotAllowed",
                    "Only user skills can be uninstalled here. Plugin and system skills are read-only.");
                return false;
            }

            return _skillFileOperations.CanEdit(skill, out reason);
        }

        private bool CanUninstallSkill(AppServerSkillInfo skill)
        {
            string reason;
            return CanUninstallSkill(skill, out reason);
        }

        private bool CanUninstallSkill(AppServerSkillInfo skill, out string reason)
        {
            if (skill == null)
            {
                reason = string.Empty;
                return false;
            }

            if (ResolveSkillSource(skill).Kind == SkillSourceKind.User && _skillFileOperations.CanEdit(skill, out reason))
            {
                return true;
            }

            reason = UiLocalization.Text(
                "SkillsManagementForm.Message.UninstallNotAllowed",
                "Only user skills can be uninstalled here. Plugin and system skills are read-only.");
            return false;
        }

        private bool CanUseSkillFileEditing()
        {
            return _skillFileOperations.GetSaveLocations().Count > 0;
        }

        private string GetSelectedSkillPath()
        {
            var selectedSkill = GetSelectedSkill();
            return selectedSkill != null ? selectedSkill.Path : null;
        }

        private void SelectSkillByPath(string skillPath)
        {
            if (string.IsNullOrWhiteSpace(skillPath))
            {
                if (_skillListView.Items.Count > 0)
                {
                    _skillListView.Items[0].Selected = true;
                    _skillListView.Items[0].Focused = true;
                }

                return;
            }

            foreach (ListViewItem item in _skillListView.Items)
            {
                var skill = item.Tag as AppServerSkillInfo;
                if (skill != null && OfficialSkillFileOperations.PathsReferToSameSkill(skill.Path, skillPath))
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    return;
                }
            }

            if (_skillListView.Items.Count > 0)
            {
                _skillListView.Items[0].Selected = true;
                _skillListView.Items[0].Focused = true;
            }
        }

        private static AppServerSkillsSnapshot SelectSnapshotForWorkspace(IEnumerable<AppServerSkillsSnapshot> snapshots, string workspacePath)
        {
            var candidates = (snapshots ?? Enumerable.Empty<AppServerSkillsSnapshot>()).Where(item => item != null).ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            var exact = candidates.FirstOrDefault(item => string.Equals(item.WorkspacePath, workspacePath, StringComparison.OrdinalIgnoreCase));
            return exact ?? candidates[0];
        }

        private static string GetSkillDisplayName(AppServerSkillInfo skill)
        {
            if (skill == null)
            {
                return UiLocalization.Text("SkillsManagementForm.Value.UnknownSkill", "(Unknown Skill)");
            }

            return !string.IsNullOrWhiteSpace(skill.DisplayName)
                ? skill.DisplayName.Trim()
                : (!string.IsNullOrWhiteSpace(skill.Name)
                    ? skill.Name.Trim()
                    : UiLocalization.Text("SkillsManagementForm.Value.UnnamedSkill", "(Unnamed Skill)"));
        }

        private static string GetSkillListSummary(AppServerSkillInfo skill)
        {
            if (skill == null)
            {
                return string.Empty;
            }

            var summary = !string.IsNullOrWhiteSpace(skill.ShortDescription)
                ? skill.ShortDescription
                : skill.Description;
            var scopeText = DescribeSkillScope(skill.Scope);

            if (string.IsNullOrWhiteSpace(summary))
            {
                return string.Format(
                    UiLocalization.Text("SkillsManagementForm.Summary.ScopeOnly", "Scope: {0}"),
                    scopeText);
            }

            summary = summary.Replace("\r", " ").Replace("\n", " ").Trim();
            summary = summary.Length <= 88 ? summary : summary.Substring(0, 88) + "...";
            return string.Format(
                UiLocalization.Text("SkillsManagementForm.Summary.ScopeWithText", "Scope: {0}; {1}"),
                scopeText,
                summary);
        }

        private static string DescribeSkillScope(string scope)
        {
            switch ((scope ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "user":
                    return UiLocalization.Text("SkillsManagementForm.Scope.User", "User");
                case "repo":
                    return UiLocalization.Text("SkillsManagementForm.Scope.Repo", "Project");
                case "system":
                    return UiLocalization.Text("SkillsManagementForm.Scope.System", "System");
                case "admin":
                    return UiLocalization.Text("SkillsManagementForm.Scope.Admin", "Admin");
                case "plugin":
                    return UiLocalization.Text("SkillsManagementForm.Scope.Plugin", "Plugin");
                default:
            return string.IsNullOrWhiteSpace(scope)
                ? UiLocalization.Text("SkillsManagementForm.Scope.Unknown", "Unknown")
                : scope;
            }
        }

        private SkillSourceInfo ResolveSkillSource(AppServerSkillInfo skill)
        {
            return SkillSourceResolver.Resolve(skill, _workspacePath, _skillSourcePlugins);
        }

        private ListView CreateSkillListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details,
                UseCompatibleStateImageBehavior = false,
                LabelWrap = false,
                AccessibleName = UiLocalization.Text("SkillsManagementForm.Accessible.Skills", "Skills"),
                TabIndex = 1
            };
            UiTheme.ApplyListView(listView);
            listView.Columns.Add(UiLocalization.Text("SkillsManagementForm.Column.Skill", "Skill"), 240, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("SkillsManagementForm.Column.Status", "Status"), 90, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("SkillsManagementForm.Column.Source", "Source"), 220, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("SkillsManagementForm.Column.Summary", "Summary"), 430, HorizontalAlignment.Left);
            return listView;
        }

        private static ComboBox CreateFilterComboBox()
        {
            var comboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("SkillsManagementForm.Accessible.Filter", "Filter"),
                TabIndex = 0
            };
            UiTheme.ApplyComboBox(comboBox);
            return comboBox;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(100, 0),
                Margin = new Padding(0, 0, 8, 0),
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
        }

        private void BuildLayout()
        {
            var actionLabel = CreateSectionLabel(UiLocalization.Text("SkillsManagementForm.Label.Actions", "Actions"));
            var actionPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0),
                Padding = new Padding(0),
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                TabIndex = 2
            };
            actionPanel.Controls.Add(_refreshButton);
            actionPanel.Controls.Add(_newButton);
            actionPanel.Controls.Add(_editButton);
            actionPanel.Controls.Add(_enableButton);
            actionPanel.Controls.Add(_disableButton);
            actionPanel.Controls.Add(_uninstallButton);
            actionPanel.Controls.Add(_openFolderButton);
            actionPanel.Controls.Add(_errorsButton);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false,
                TabIndex = 0
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(BuildInstalledTabPanel(), 0, 0);
            root.Controls.Add(actionLabel, 0, 1);
            root.Controls.Add(actionPanel, 0, 2);
            root.Controls.Add(_statusLabel, 0, 3);

            Controls.Add(root);
        }

        private TableLayoutPanel BuildInstalledTabPanel()
        {
            var filterPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                TabIndex = 0
            };
            filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            filterPanel.Controls.Add(CreateSectionLabel(UiLocalization.Text("SkillsManagementForm.Label.Filter", "Filter")), 0, 0);
            filterPanel.Controls.Add(_filterComboBox, 1, 0);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.InnerPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(filterPanel, 0, 0);
            root.Controls.Add(CreateSectionLabel(UiLocalization.Text("SkillsManagementForm.Label.Skills", "Skills")), 0, 1);
            root.Controls.Add(_skillListView, 0, 2);
            return root;
        }

        private static Label CreateSectionLabel(string text)
        {
            var label = new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Height = 24,
                Margin = new Padding(0, 8, 8, 8),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }
    }

    internal enum SkillStatusFilter
    {
        All,
        Enabled,
        Disabled
    }
}
