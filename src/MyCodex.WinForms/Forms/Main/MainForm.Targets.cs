using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private TargetCatalogStore _targetCatalog;
        private TargetProfileSnapshot _fixedTarget;
        private TargetInstanceCoordinator _targetInstance;
        private TargetWindowLauncher _targetLauncher;
        private string _targetChannel;
        private MenuItem _targetsMenuItem;
        private TargetManagerForm _targetManagerForm;
        private Timer _targetStatusTimer;
        private bool _targetEditBusy;
        private bool _targetReconnectPending;
        private bool _targetStartupComplete;

        internal void ConfigureTargetContext(TargetCatalogStore catalog, TargetInstanceCoordinator instance, string channelId)
        {
            _targetCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _targetInstance = instance ?? throw new ArgumentNullException(nameof(instance));
            _targetChannel = channelId;
            _fixedTarget = catalog.GetTarget(Path.GetFileName(_appConfigService.GetAppDataDirectory().TrimEnd(Path.DirectorySeparatorChar)));
            if (_isPersistenceRecoveryPending) ResetDefaultTargetAfterPersistenceFailure();
            InitializeTargetWindowGroup();
            ConfigureAllTargetExitMenus();
            _targetLauncher = new TargetWindowLauncher(catalog, channelId, catalog.GetSharedRoot());
            _targetsMenuItem = new MenuItem(UiLocalization.Text("Settings.Menu.Targets", "Targets(&T)"));
            AssembleSettingsAndMainMenu();
            _targetsMenuItem.Popup += (s, e) => RefreshTargetsMenu();
            ApplyTargetMenuText();
            _targetsMenuItem.MenuItems.Add(_appServerRuntimeSettingsMenuItem);
            RefreshTargetsMenu();
            _targetStatusTimer = new Timer { Interval = 1000 };
            _targetStatusTimer.Tick += async (s, e) =>
            {
                PublishTargetStatus(TargetInstanceLifecycle.Running);
                await RefreshSharedRemoteServiceAsync();
            };
            FormClosed += (s, e) => _targetStatusTimer.Dispose();
            RefreshWindowTitle();
            UpdateTargetTrayText();
            RefreshMainWindowHotKeyRegistration();
        }

        private void ApplyTargetMenuText()
        {
            if (_targetsMenuItem == null) return;
            _targetsMenuItem.Text = UiLocalization.Text("Settings.Menu.Targets", "Targets(&T)");
            _appServerRuntimeSettingsMenuItem.Text = UiLocalization.Text("Settings.Menu.TargetManager", "Target Manager(&G)...");
        }

        private void CompleteTargetStartup()
        {
            _targetStartupComplete = true;
            if (_isPersistenceRecoveryPending)
            {
                ResetDefaultTargetAfterPersistenceFailure();
                return;
            }
            if (_targetInstance == null || _isExitRequested || IsDisposed) return;
            try { _targetCatalog.RememberOpened(_fixedTarget.Id); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Unable to remember opened target: {0}", ex.GetType().Name); }
            PublishTargetStatus(TargetInstanceLifecycle.Running);
            _targetStatusTimer.Start();
        }

        private void ResetDefaultTargetAfterPersistenceFailure()
        {
            if (_targetCatalog == null || _fixedTarget == null) return;
            try { _targetCatalog.ResetLastOpenedAfterFailure(_fixedTarget.Id); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Unable to reset failed startup target: {0}", ex.GetType().Name); }
        }

        private void RefreshTargetsMenu()
        {
            try
            {
                var catalog = _targetCatalog.Load();
                _targetsMenuItem.MenuItems.Remove(_appServerRuntimeSettingsMenuItem);
                while (_targetsMenuItem.MenuItems.Count > 0)
                {
                    var item = _targetsMenuItem.MenuItems[0];
                    _targetsMenuItem.MenuItems.Remove(item);
                    item.Dispose();
                }
                foreach (var target in catalog.Targets)
                {
                    var id = target.Id;
                    var item = new MenuItem(TargetManagerForm.DisplayName(target).Replace("&", "&&"))
                    { Checked = id == _fixedTarget.Id, Tag = target };
                    item.Click += async (s, e) => await OpenTargetWindowAsync(id);
                    _targetsMenuItem.MenuItems.Add(item);
                }
                _targetsMenuItem.MenuItems.Add("-");
                _targetsMenuItem.MenuItems.Add(_appServerRuntimeSettingsMenuItem);
                ApplyTargetMenuText();
            }
            catch (Exception ex) { SetHighPriorityStatus(UiLocalization.Text("Program.Target.StartupFailed", "The target could not be opened. Review it in the target manager.") + " (" + ex.GetType().Name + ")"); }
        }

        private void ShowTargetManager()
        {
            if (_targetManagerForm != null && !_targetManagerForm.IsDisposed)
            {
                ActivateRuntimeWindow(_targetManagerForm);
                return;
            }
            using (var form = new TargetManagerForm(_targetCatalog, _targetChannel, _appDisplayName,
                SaveTargetRuntimeSettingsAsync, _appServerClient, _fixedTarget.Id, removeAsync: RemoveTargetFromManagerAsync))
            {
                _targetManagerForm = form;
                try { form.ShowDialog(this); }
                finally { _targetManagerForm = null; }
            }
        }

        private async Task OpenTargetWindowAsync(string id)
        {
            try
            {
                var result = await _targetLauncher.OpenAsync(id);
                if (!result.Success && !IsTargetStopping) MessageBox.Show(this, UiLocalization.Text("Program.Target.ActivationFailed", "The target did not confirm activation. It may be starting or closing; retry later or review its settings in the target manager.") + Environment.NewLine + result.Error, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                // The user closed the newly opened target before startup completed.
            }
        }

        private async Task<TargetInstanceReply> SaveTargetRuntimeSettingsAsync(string id, TargetRuntimeSettings settings)
        {
            return id == _fixedTarget.Id ? await ApplyOwnedTargetRuntimeSettingsAsync(settings) : await _targetLauncher.SaveAsync(id, settings);
        }

        internal Task<TargetInstanceReply> HandleTargetInstanceRequestAsync(TargetInstanceRequest request)
        {
            var completion = new TaskCompletionSource<TargetInstanceReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (IsDisposed || Disposing || _isExitRequested || !IsHandleCreated)
            {
                completion.SetResult(TargetInstanceReply.Failure(UiLocalization.Text("TargetManagerForm.Error.Closing", "This target is closing or starting. Please retry later.")));
                return completion.Task;
            }
            try
            {
                BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        if (IsDisposed || Disposing || _isExitRequested)
                        {
                            completion.TrySetResult(TargetInstanceReply.Failure(UiLocalization.Text("TargetManagerForm.Error.Closing", "This target is closing or starting. Please retry later.")));
                            return;
                        }
                        TargetInstanceReply reply;
                        if (request.Operation == TargetInstanceOperation.Status) reply = new TargetInstanceReply { Success = true };
                        else if (request.Operation == TargetInstanceOperation.Activate)
                        {
                            if (_isPersistenceRecoveryPending)
                            {
                                completion.TrySetResult(TargetInstanceReply.Failure(UiLocalization.Text("MainForm.Status.PersistenceRecoveryRequired", "Persisted data requires recovery. Automatic saves are disabled.")));
                                return;
                            }
                            var selection = TargetStartupPlanner.Resolve(_targetCatalog.Load(), _fixedTarget.Id, request.WorkspacePath);
                            ActivateRuntimeWindow(this);
                            if (!string.IsNullOrEmpty(selection.WorkspacePath))
                            {
                                if (FindVisibleModalDialog(this) != null) throw new InvalidOperationException(UiLocalization.Text("TargetManagerForm.Error.Busy", "This target is busy with another settings or modal operation. Finish it and retry."));
                                await LoadCommandLineWorkspaceAsync(selection.WorkspacePath);
                            }
                            _targetCatalog.RememberOpened(_fixedTarget.Id);
                            reply = new TargetInstanceReply { Success = true };
                        }
                        else if (request.Operation == TargetInstanceOperation.ApplyRuntimeSettings)
                        {
                            ActivateRuntimeWindow(this);
                            reply = FindVisibleModalDialog(this) != null
                                ? TargetInstanceReply.Failure(UiLocalization.Text("TargetManagerForm.Error.Busy", "This target is busy with another settings or modal operation. Finish it and retry."))
                                : await ApplyOwnedTargetRuntimeSettingsAsync(request.RuntimeSettings);
                        }
                        else if (request.Operation == TargetInstanceOperation.PrepareExit)
                        {
                            var ready = await PrepareTargetExitAsync(request.OperationId);
                            reply = new TargetInstanceReply
                            {
                                Success = ready,
                                ExitPreparation = ready ? MyCodex.Infrastructure.Instances.TargetExitPreparation.Ready
                                    : MyCodex.Infrastructure.Instances.TargetExitPreparation.Rejected,
                                Error = ready ? null : UiLocalization.Text("TargetExitAllForm.Status.PrepareCancelled", "Exit preparation was cancelled or could not be saved.")
                            };
                        }
                        else if (request.Operation == TargetInstanceOperation.AbortExit)
                        {
                            CancelTargetExitPreparation(request.OperationId);
                            reply = new TargetInstanceReply { Success = true };
                        }
                        else if (request.Operation == TargetInstanceOperation.CommitExit)
                        {
                            reply = new TargetInstanceReply { Success = CommitTargetExit(request.OperationId) };
                            if (reply.Success) reply.AfterResponse = () =>
                            {
                                if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(CloseCommittedTarget));
                            };
                        }
                        else if (request.Operation == TargetInstanceOperation.RemoveTarget)
                        {
                            reply = _fixedTarget.Id == "windows"
                                ? await RemoveTargetFromOwnerAsync(request.RemovalTargetId)
                                : TargetInstanceReply.Failure(UiLocalization.Text("MainForm.Removal.UseLocal", "The local target handles target removal."));
                        }
                        else reply = TargetInstanceReply.Failure("Unsupported target operation.");
                        reply.Status = BuildTargetStatus(_isExitRequested ? TargetInstanceLifecycle.Closing : TargetInstanceLifecycle.Running);
                        completion.TrySetResult(reply);
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetResult(TargetInstanceReply.Failure(UiLocalization.Text("Program.Target.StartupFailed", "The target could not be opened. Review it in the target manager.") + " (" + ex.GetType().Name + ")"));
                    }
                }));
            }
            catch (InvalidOperationException) { completion.TrySetResult(TargetInstanceReply.Failure(UiLocalization.Text("TargetManagerForm.Error.Closing", "This target is closing or starting. Please retry later."))); }
            return completion.Task;
        }

        private async Task<TargetInstanceReply> ApplyOwnedTargetRuntimeSettingsAsync(TargetRuntimeSettings settings)
        {
            if (!_targetStartupComplete || _isExitRequested || _isPersistenceRecoveryPending || _targetEditBusy || _isRuntimeWindowTransitionInProgress)
                return TargetInstanceReply.Failure(UiLocalization.Text("TargetManagerForm.Error.Busy", "This target is busy with another settings or modal operation. Finish it and retry."));
            _targetEditBusy = true;
            var settingsSaved = false;
            try
            {
                var planner = new TargetRuntimeSettingsPlanner();
                var previous = _appConfigStore.Load();
                planner.ValidateRevision(_fixedTarget, previous, settings, true);
                if (_fixedTarget.Kind == "Ssh" && !TargetRuntimeSettingsPlanner.IsSameSshHost(
                    planner.Build(_fixedTarget, previous).RemoteSsh.HostName, settings.RemoteSsh?.HostName))
                    return TargetInstanceReply.Failure(UiLocalization.Text("TargetSettingsForm.Error.HostRequiresClosed",
                        "Close this target before changing its SSH host. Your draft is retained."));
                var restart = planner.RequiresReconnect(_fixedTarget, previous, settings) || _targetReconnectPending;
                if (restart && !PreflightRuntimeWindowsForTransition())
                    return TargetInstanceReply.Failure(UiLocalization.Text("TargetManagerForm.Error.Busy", "This target is busy with another settings or modal operation. Finish it and retry."));
                var reconnect = restart && (IsConnectedStatus() || _targetReconnectPending || HasActiveRuntimeWorkForExit());
                if (reconnect && MessageBox.Show(FindVisibleModalDialog(this) ?? this,
                    UiLocalization.Text("TargetManagerForm.Message.Restart", "Save these runtime settings and reconnect this target? Active responses, commands and automations on this target will stop."),
                    AppConstants.AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
                    return TargetInstanceReply.Failure(UiLocalization.Text("TargetManagerForm.Error.Cancelled", "Changes were cancelled; your draft is retained."));
                if (_isExitRequested) return TargetInstanceReply.Failure(UiLocalization.Text("TargetManagerForm.Error.Closing", "This target is closing or starting. Please retry later."));
                var selectedMode = _currentInteractionMode;
                var previousProfile = _runtimeProfileService.GetCurrentRuntimeProfileId(previous);
                SaveCurrentComposeState();
                CaptureCurrentModeSelection();
                if (!TryPersistState()) return TargetInstanceReply.Failure(UiLocalization.Text("MainForm.Status.SaveStateFailedPrefix", "Save state failed: "));
                if (reconnect) _targetReconnectPending = true;
                _fixedTarget = _targetCatalog.SaveRuntimeSettings(_fixedTarget.Id, settings);
                settingsSaved = true;
                LoadAppConfig();
                if (!await EnsureBundledRuntimeUpdatedAsync())
                    return new TargetInstanceReply { SettingsSaved = true, Error = UiLocalization.Text("BundledRuntimeUpdateForm.Incomplete", "The runtime update did not finish.") };
                _globalServerConfigSnapshot = null;
                RefreshWindowTitle();
                UpdateTargetTrayText();
                if (reconnect)
                {
                    await RestartCurrentRuntimeAfterSaveAsync(selectedMode, null, null);
                    LoadAppConfig();
                    _targetReconnectPending = !IsConnectedStatus();
                    if (_targetReconnectPending) return new TargetInstanceReply { SettingsSaved = true, Error = UiLocalization.Text("TargetManagerForm.Error.Reconnect", "Settings were saved, but reconnection failed. Save again to retry.") };
                }
                else await ReloadStateAfterRuntimeSettingsSaveAsync(previousProfile);
                SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.AppServerRuntimeSaved", "App Server runtime settings saved"));
                PublishTargetStatus(TargetInstanceLifecycle.Running);
                return new TargetInstanceReply { Success = true, SettingsSaved = true, Status = BuildTargetStatus(TargetInstanceLifecycle.Running) };
            }
            catch (Exception ex)
            {
                return new TargetInstanceReply { SettingsSaved = settingsSaved, Error = TargetManagerForm.FormatSettingsError(ex) };
            }
            finally { _targetEditBusy = false; }
        }

        private TargetInstanceStatus BuildTargetStatus(TargetInstanceLifecycle lifecycle)
        {
            if (lifecycle == TargetInstanceLifecycle.Running && (!_targetStartupComplete || _isPersistenceRecoveryPending)) lifecycle = TargetInstanceLifecycle.Starting;
            return new TargetInstanceStatus
            {
                Lifecycle = lifecycle, IsWorking = HasActiveRuntimeWorkForExit(),
                HasPendingInteraction = _asyncInputRequests.Count > 0 || _remoteInteractionDialogs.HasPending || _approvalRequestDialogQueue.HasPending,
                IsConnected = IsConnectedStatus(), ConnectionDetail = _connectionStatus,
                IsPreparingBundledRuntime = _bundledRuntimePreparationCount > 0,
                IsExitPrepared = IsTargetExitPrepared,
                ActiveWorkCount = CountActiveTargetWork(), UnsavedWindowCount = CountUnsavedTargetWindows()
            };
        }

        private void PublishTargetStatus(TargetInstanceLifecycle lifecycle)
        {
            if (_targetInstance == null) return;
            if (_isExitRequested) lifecycle = TargetInstanceLifecycle.Closing;
            var status = BuildTargetStatus(lifecycle);
            _targetInstance.UpdateStatus(status);
            UpdateTargetTrayText(status);
        }

        private void MarkTargetClosing()
        {
            _targetStatusTimer?.Stop();
            PublishTargetStatus(TargetInstanceLifecycle.Closing);
        }

    }
}
