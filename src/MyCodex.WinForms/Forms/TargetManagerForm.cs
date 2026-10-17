using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class TargetManagerForm : Form
    {
        private readonly TargetCatalogStore _catalog;
        private readonly string _channelId;
        private readonly string _currentTargetId;
        private readonly Func<string, TargetRuntimeSettings, Task<TargetInstanceReply>> _save;
        private readonly Func<string, Task<TargetInstanceReply>> _removeAsync;
        private readonly IAppServerWindowsSandboxClient _sandbox;
        private readonly ListView _targets;
        private readonly Label _status;
        private readonly Button _refresh;
        private readonly Button _addWsl;
        private readonly Button _addSsh;
        private readonly Button _edit;
        private readonly Button _remove;
        private readonly Button _close;
        private IReadOnlyList<TargetInstanceReply> _instances = new TargetInstanceReply[0];
        private bool _busy;

        public TargetManagerForm(TargetCatalogStore catalog, string channelId, string appDisplayName,
            Func<string, TargetRuntimeSettings, Task<TargetInstanceReply>> saveAsync = null,
            IAppServerWindowsSandboxClient sandbox = null, string currentTargetId = null,
            string initialError = null, Func<string, Task<TargetInstanceReply>> removeAsync = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _channelId = channelId;
            _currentTargetId = currentTargetId;
            _sandbox = sandbox;
            _removeAsync = removeAsync;
            _save = saveAsync ?? new TargetWindowLauncher(catalog, channelId, catalog.GetSharedRoot()).SaveAsync;
            Text = UiLocalization.Text("Window.Targets.All", "All targets") + " - " +
                UiLocalization.Text("TargetManagerForm.Title", "Target Manager") + " - " + appDisplayName;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(680, 380);
            Size = new Size(820, 500);
            _targets = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                HideSelection = false, TabIndex = 0,
                AccessibleName = UiLocalization.Text("TargetManagerForm.Accessible.Targets", "Targets")
            };
            _targets.Columns.Add(UiLocalization.Text("TargetManagerForm.Column.Name", "Name"), 220);
            _targets.Columns.Add(UiLocalization.Text("TargetManagerForm.Column.Platform", "Platform"), 100);
            _targets.Columns.Add(UiLocalization.Text("TargetManagerForm.Column.State", "State"), 220);
            _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), TabStop = false, Text = initialError ?? string.Empty };
            _refresh = CreateButton("TargetManagerForm.Button.Refresh", "Refresh(&R)", "TargetManagerForm.Accessible.Button.Refresh", "Refresh", 0);
            _addWsl = CreateButton("TargetManagerForm.Button.AddWsl", "Add WSL(&W)...", "TargetManagerForm.Accessible.Button.AddWsl", "Add WSL", 1);
            _addSsh = CreateButton("TargetManagerForm.Button.AddSsh", "Add SSH(&S)...", "TargetManagerForm.Accessible.Button.AddSsh", "Add SSH", 2);
            _edit = CreateButton("TargetManagerForm.Button.Edit", "Edit(&E)...", "TargetManagerForm.Accessible.Button.Edit", "Edit", 3);
            _remove = CreateButton("TargetManagerForm.Removal.Button.Remove", "Remove(&D)...", "TargetManagerForm.Removal.Accessible.Remove", "Remove target", 4);
            _close = CreateButton("TargetManagerForm.Button.Close", "Close(&C)", "TargetManagerForm.Accessible.Button.Close", "Close", 5);
            FormInteractionConventions.ConfigureRefreshButton(_refresh);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), TabStop = false, TabIndex = 1 };
            buttons.Controls.AddRange(new Control[] { _refresh, _addWsl, _addSsh, _edit, _remove, _close });
            Controls.Add(_targets);
            Controls.Add(_status);
            Controls.Add(buttons);
            CancelButton = _close;
            _targets.SelectedIndexChanged += (s, e) => UpdateSelectionActions();
            _refresh.Click += async (s, e) => await RefreshTargetsAsync();
            _edit.Click += async (s, e) => await EditTargetAsync(SelectedTarget, false);
            _remove.Click += async (s, e) => await RemoveTargetAsync(SelectedTarget);
            _addWsl.Click += async (s, e) => await EditTargetAsync(new TargetProfileSnapshot { Kind = "Wsl" }, true);
            _addSsh.Click += async (s, e) => await EditTargetAsync(new TargetProfileSnapshot { Kind = "Ssh" }, true);
            _close.Click += (s, e) => Close();
            Shown += async (s, e) =>
            {
                _targets.Focus();
                await RefreshTargetsAsync();
            };
            FormClosing += (s, e) => { if (_busy) e.Cancel = true; };
            UiTheme.ApplyForm(this);
            UiTheme.ApplyListView(_targets);
            ActiveControl = _targets;
            UpdateSelectionActions();
        }

        public static void ShowStartup(TargetCatalogStore catalog, string channelId, string appDisplayName, string error)
        {
            try
            {
                var selection = TargetStartupPlanner.Resolve(catalog.Load(), null, null);
                Program.InitializeUiPreferences(new JsonAppConfigStore(catalog.GetPaths(selection.Target.Id)));
            }
            catch
            {
                UiTheme.InitializeUiFontScale(100);
            }
            using (var manager = new TargetManagerForm(catalog, channelId, appDisplayName, initialError: error))
                manager.ShowDialog();
        }

        private TargetProfileSnapshot SelectedTarget
        {
            get { return _targets.SelectedItems.Count == 0 ? null : _targets.SelectedItems[0].Tag as TargetProfileSnapshot; }
        }

        private async Task RefreshTargetsAsync(string selectedId = null)
        {
            if (_busy) return;
            selectedId = selectedId ?? SelectedTarget?.Id ?? _currentTargetId;
            SetBusy(true);
            try
            {
                var catalog = await Task.Run(() => _catalog.Load());
                _instances = await Task.Run(() => TargetInstanceCoordinator.Discover(_channelId, _catalog.GetSharedRoot()));
                _targets.Items.Clear();
                foreach (var target in catalog.Targets)
                {
                    var row = new ListViewItem(DisplayName(target)) { Tag = target };
                    row.SubItems.Add(PlatformName(target));
                    row.SubItems.Add(StatusText(_instances.FirstOrDefault(r => r.Instance?.TargetId == target.Id)));
                    _targets.Items.Add(row);
                    if (target.Id == selectedId) row.Selected = row.Focused = true;
                }
                if (_targets.SelectedItems.Count == 0 && _targets.Items.Count > 0)
                    _targets.Items[0].Selected = _targets.Items[0].Focused = true;
            }
            catch (Exception ex) { ShowFailure(ex); }
            finally { SetBusy(false); }
        }

        private void UpdateSelectionActions()
        {
            _edit.Enabled = !_busy && SelectedTarget != null;
            _remove.Enabled = !_busy && _removeAsync != null && SelectedTarget != null && SelectedTarget.Kind != "Windows";
            if (!_busy && _removeAsync != null && SelectedTarget?.Kind == "Windows")
                _status.Text = UiLocalization.Text("TargetManagerForm.Removal.Status.Windows", "The local Windows target cannot be removed.");
            else if (!_busy && _status.Text == UiLocalization.Text("TargetManagerForm.Removal.Status.Windows", "The local Windows target cannot be removed."))
                _status.Text = string.Empty;
        }

        private async Task RemoveTargetAsync(TargetProfileSnapshot target)
        {
            if (_busy || target == null || target.Kind == "Windows" || _removeAsync == null) return;
            using (var confirmation = CreateRemovalConfirmation(target))
                if (confirmation.ShowDialog(this) != DialogResult.OK) { _targets.Focus(); return; }
            SetBusy(true);
            var selectedId = target.Id;
            var outcome = string.Empty;
            try
            {
                _status.Text = string.Format(UiLocalization.Text("TargetManagerForm.Removal.Status.Removing", "Removing {0}..."), DisplayName(target));
                var result = await _removeAsync(target.Id);
                if (result == null) throw new InvalidOperationException("Target removal returned no result.");
                _status.Text = !string.IsNullOrWhiteSpace(result.Error) ? result.Error :
                    string.Format(UiLocalization.Text(result.Success ? "TargetManagerForm.Removal.Status.Removed" : "TargetManagerForm.Removal.Status.Failed",
                        result.Success ? "Removed {0}." : "Could not remove {0}. Review its current state before retrying."), DisplayName(target));
                if (result.Success) selectedId = _currentTargetId;
            }
            catch (Exception ex)
            {
                _status.Text = string.Format(UiLocalization.Text("TargetManagerForm.Removal.Status.Failed", "Could not remove {0}. Review its current state before retrying."), DisplayName(target)) + " (" + ex.GetType().Name + ")";
            }
            finally { outcome = _status.Text; SetBusy(false); }
            await RefreshTargetsAsync(selectedId);
            _status.Text = outcome;
            _targets.Focus();
        }

        internal static Form CreateRemovalConfirmation(TargetProfileSnapshot target)
        {
            var dialog = new Form
            {
                Text = UiLocalization.Text("TargetManagerForm.Removal.Title", "Remove target") + " - " + DisplayName(target),
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MinimumSize = new Size(540, 260), Size = new Size(640, 300), MinimizeBox = false, MaximizeBox = false
            };
            var message = new Label
            {
                Text = string.Format(UiLocalization.Text("TargetManagerForm.Removal.Message", "Remove {0}? This removes its MyCodex local settings and pairing information. Projects, Git repositories, remote Codex data and Office documents are kept. Remove the old device entry on your phone manually. A running target must exit normally first; cancellation keeps this target."), DisplayName(target)),
                Dock = DockStyle.Fill, Padding = new Padding(16), TabStop = false
            };
            UiTheme.ApplyLabel(message, false);
            var remove = CreateButton("TargetManagerForm.Removal.Button.Confirm", "Remove this target(&D)", "TargetManagerForm.Removal.Accessible.Remove", "Remove target", 0);
            remove.DialogResult = DialogResult.OK;
            var cancel = CreateButton("Common.Cancel", "&Cancel", "Common.Accessible.Cancel", "Cancel", 1);
            cancel.DialogResult = DialogResult.Cancel;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft, TabStop = false };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(remove);
            dialog.Controls.Add(message);
            dialog.Controls.Add(buttons);
            dialog.AcceptButton = dialog.CancelButton = cancel;
            dialog.ActiveControl = cancel;
            dialog.Shown += (s, e) => cancel.Focus();
            UiTheme.ApplyForm(dialog);
            return dialog;
        }

        private async Task EditTargetAsync(TargetProfileSnapshot target, bool isNew)
        {
            if (_busy || target == null) return;
            SetBusy(true);
            string selectedId = target.Id;
            try
            {
                var settings = isNew ? new TargetRuntimeSettings
                {
                    Name = target.Kind == "Wsl" ? "WSL" : "SSH",
                    CodexRuntimeSource = target.Kind == "Wsl" ? CodexRuntimeSources.Wsl : CodexRuntimeSources.RemoteUnixSsh,
                    WslAppServer = new WslAppServerConfigSnapshot(), RemoteSsh = new RemoteMacSshConfigSnapshot()
                } : _catalog.LoadRuntimeSettings(target.Id);
                var paths = isNew ? new AppConfigService(_catalog.GetSharedRoot(), AppDomain.CurrentDomain.BaseDirectory,
                    string.Equals(_channelId, "Dev", StringComparison.OrdinalIgnoreCase) ? "MyCodex-Dev" : "MyCodex", _catalog.GetSharedRoot()) : _catalog.GetPaths(target.Id);
                WslAppServerSettingsForm editor = null;
                using (editor = new WslAppServerSettingsForm(target, isNew, settings,
                    new CodexRuntimeManager(paths), new CodexRuntimeUpdateService(paths),
                    target.Id == _currentTargetId && target.Kind == "Windows" ? _sandbox : null, null,
                    async next =>
                    {
                        try
                        {
                            TargetInstanceReply result;
                            if (isNew)
                            {
                                var created = target.Kind == "Wsl" ? _catalog.AddWsl(next.Name, next.WslAppServer) : _catalog.AddSsh(next.Name, next.RemoteSsh);
                                selectedId = created.Id;
                                // Retain the saved identity if a later settings operation needs retrying.
                                target = created;
                                isNew = false;
                                editor.MarkTargetSaved(_catalog.LoadRuntimeSettings(created.Id));
                                result = new TargetInstanceReply { Success = true };
                            }
                            else result = await _save(target.Id, next);
                            if (result.SettingsSaved || result.Success) editor.MarkTargetSaved(_catalog.LoadRuntimeSettings(target.Id));
                            if (!result.Success) editor.SetTargetSaveStatus(result.Error);
                            return result.Success;
                        }
                        catch (DuplicateTargetException ex)
                        {
                            selectedId = ex.ExistingTargetId;
                            editor.SetTargetSaveStatus(UiLocalization.Text("TargetManagerForm.Error.Duplicate", "This target already exists. Edit it here, or open it from the Targets menu."));
                            return false;
                        }
                        catch (Exception ex) { editor.SetTargetSaveStatus(FormatSettingsError(ex)); return false; }
                    }, new WslDistributionService().ListAsync))
                    editor.ShowDialog(this);
            }
            catch (Exception ex) { ShowFailure(ex); }
            finally { SetBusy(false); }
            await RefreshTargetsAsync(selectedId);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _refresh.Enabled = _addWsl.Enabled = _addSsh.Enabled = _close.Enabled = !busy;
            UpdateSelectionActions();
        }

        private void ShowFailure(Exception ex) { _status.Text = SafeError(ex); }
        internal static string FormatSettingsError(Exception ex)
        {
            if (ex is TargetRuntimeSettingsConflictException) return UiLocalization.Text("TargetSettingsForm.Error.Changed", "This target's runtime settings changed after this editor was opened. Your draft is retained. Reopen the editor to load the latest settings before applying changes.");
            if (ex is AggregateException) return UiLocalization.Text("TargetSettingsForm.Error.RestoreUnknown", "Saving failed and the original settings could not be fully restored. Your draft is retained. Close this editor and inspect this target's files before retrying.");
            return string.Format(UiLocalization.Text("TargetSettingsForm.Status.SaveFailed.Template", "Save failed: {0}"), ex.GetType().Name);
        }
        private static string SafeError(Exception ex)
        {
            return UiLocalization.Text("Program.Target.StartupFailed", "The target could not be opened. Review it in the target manager.") + " (" + ex.GetType().Name + ")";
        }

        internal static string PlatformName(TargetProfileSnapshot target)
        {
            return target.Kind == "Wsl" ? "WSL" : target.Kind == "Ssh" ? "SSH" : "Windows";
        }

        internal static string DisplayName(TargetProfileSnapshot target)
        {
            return target.Kind == "Windows" && string.Equals(target.Name, "Windows", StringComparison.OrdinalIgnoreCase)
                ? UiLocalization.Text("Target.Name.Local", "This PC") : target.Name;
        }

        internal static string StatusText(TargetInstanceReply reply)
        {
            if (reply == null) return UiLocalization.Text("Target.Status.NotRunning", "Not running");
            if (!reply.Success || reply.Status == null) return UiLocalization.Text("Target.Status.Unavailable", "Unavailable");
            if (reply.Status.Lifecycle == TargetInstanceLifecycle.Starting) return UiLocalization.Text("Target.Status.Starting", "Starting");
            if (reply.Status.Lifecycle == TargetInstanceLifecycle.Closing) return UiLocalization.Text("Target.Status.Closing", "Closing");
            if (reply.Status.HasPendingInteraction) return UiLocalization.Text("Target.Status.Pending", "Waiting for interaction");
            if (reply.Status.IsWorking) return UiLocalization.Text("Target.Status.Working", "Working");
            return reply.Status.IsConnected ? UiLocalization.Text("Target.Status.Running", "Running") :
                string.IsNullOrWhiteSpace(reply.Status.ConnectionDetail) ? UiLocalization.Text("Target.Status.Failed", "Connection failed") : reply.Status.ConnectionDetail;
        }

        private static Button CreateButton(string key, string fallback, string accessibleKey, string accessibleFallback, int tabIndex)
        {
            var button = new Button { Text = UiLocalization.Text(key, fallback), AccessibleName = UiLocalization.Text(accessibleKey, accessibleFallback), AutoSize = true, TabIndex = tabIndex, MinimumSize = new Size(90, 32) };
            UiTheme.ApplyDialogButton(button, UiGlyph.Ok);
            return button;
        }
    }
}
