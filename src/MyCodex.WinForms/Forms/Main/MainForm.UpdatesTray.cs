using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.Updates;
using MyCodex.Shared.Constants;
using MyCodex.WinForms;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly MenuItem _targetTrayMenuItem = new MenuItem { Enabled = false };
        private readonly MenuItem _mainWindowHotKeyTrayMenuItem = new MenuItem { Enabled = false };

        private void ConfigureTrayIcon()
        {
            _showMainWindowTrayMenuItem.Click += ShowMainWindowTrayMenuItem_Click;
            _hideMainWindowTrayMenuItem.Click += HideMainWindowTrayMenuItem_Click;
            _exitTrayMenuItem.Click += ExitTrayMenuItem_Click;
            _trayContextMenu.Popup += TrayContextMenu_Popup;
            _trayContextMenu.MenuItems.Add(_targetTrayMenuItem);
            _trayContextMenu.MenuItems.Add(_mainWindowHotKeyTrayMenuItem);
            _trayContextMenu.MenuItems.Add("-");
            _trayContextMenu.MenuItems.Add(_showMainWindowTrayMenuItem);
            _trayContextMenu.MenuItems.Add(_hideMainWindowTrayMenuItem);
            _trayContextMenu.MenuItems.Add("-");
            _trayContextMenu.MenuItems.Add(_exitTrayMenuItem);

            _trayIcon.Text = _appDisplayName;
            _trayIcon.Icon = Icon ?? SystemIcons.Application;
            _trayIcon.ContextMenu = _trayContextMenu;
            _trayIcon.Visible = true;
            _trayIcon.MouseClick += TrayIcon_MouseClick;
            _trayIcon.DoubleClick += TrayIcon_DoubleClick;
        }

        private void UpdateTargetTrayText(TargetInstanceStatus status = null)
        {
            if (_trayIcon == null) return;
            status = status ?? BuildTargetStatus(_isExitRequested ? TargetInstanceLifecycle.Closing : TargetInstanceLifecycle.Running);
            var statusText = TargetManagerForm.StatusText(new TargetInstanceReply { Success = true, Status = status });
            _trayIcon.Text = TargetTrayTextFormatter.BuildTrayTooltip(
                _appDisplayName, GetCurrentRuntimeDisplayName(), statusText);
            RefreshTargetTrayMenuText(statusText);
        }

        private void RefreshTargetTrayMenuText(string statusText = null)
        {
            var target = GetCurrentRuntimeDisplayName();
            if (statusText == null)
                statusText = TargetManagerForm.StatusText(new TargetInstanceReply
                { Success = true, Status = BuildTargetStatus(_isExitRequested ? TargetInstanceLifecycle.Closing : TargetInstanceLifecycle.Running) });
            _targetTrayMenuItem.Text = (target + " — " + statusText).Replace("&", "&&");
            _mainWindowHotKeyTrayMenuItem.Text = string.Format(
                UiLocalization.Text("MainForm.Tray.TargetHotKey", "Visibility shortcut: {0}"),
                GetMainWindowHotKeyStateText()).Replace("&", "&&");
            _exitTrayMenuItem.Text = string.Format(
                UiLocalization.Text("MainForm.Tray.ExitTarget", "Exit {0}(&X)..."), target.Replace("&", "&&"));
        }

        private async void OnCheckUpdatesMenuItemClick(object sender, EventArgs e)
        {
            await CheckForUpdatesAsync(false);
        }

        private async Task RunAutomaticUpdateCheckAsync()
        {
            if (_hasAutomaticUpdateCheckRun || !AutomaticUpdateCheckGate.TryClaim(_targetInstance?.InstanceRecord?.ChannelId))
            {
                return;
            }

            _hasAutomaticUpdateCheckRun = true;
            await CheckForUpdatesAsync(true);
        }

        private async Task CheckForUpdatesAsync(bool automatic)
        {
            if (!await WaitForTargetExitDecisionAsync()) return;
            if (_appConfigSnapshot == null)
            {
                LoadAppConfig();
            }

            if (_isUpdateCheckInProgress)
            {
                if (!automatic)
                {
                    SetStatus(UiLocalization.Text("MainForm.Update.Status.CheckAlreadyRunning", "Update check is already running"));
                }

                return;
            }

            _isUpdateCheckInProgress = true;
            _checkUpdatesMenuItem.Enabled = false;
            if (!automatic)
            {
                SetStatus(UiLocalization.Text("MainForm.Update.Status.Checking", "Checking for updates..."));
            }

            try
            {
                var service = new AppUpdateService(_appConfigService, _appConfigSnapshot.UpdateSettings);
                using (var cancellationTokenSource = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(_targetShutdownCancellation.Token))
                {
                    cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(60));
                    var check = await service.CheckAsync(GetCurrentAppVersion(), cancellationTokenSource.Token);
                    if (!await WaitForTargetExitDecisionAsync()) return;
                    if (!check.IsConfigured)
                    {
                        if (!automatic)
                        {
                            MessageBox.Show(
                                this,
                                UiLocalization.Text(
                                    "MainForm.Update.Message.ManifestNotConfigured",
                                    "Update manifest URL is not configured."),
                                AppConstants.AppName,
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }

                        SetStatus(UiLocalization.Text("MainForm.Update.Status.ManifestNotConfigured", "Update manifest is not configured"));
                        return;
                    }

                    if (!check.IsUpdateAvailable)
                    {
                        if (!automatic)
                        {
                            MessageBox.Show(
                                this,
                                string.Format(
                                    UiLocalization.Text(
                                        "MainForm.Update.Message.AlreadyLatest.Template",
                                        "MyCodex is up to date.\r\n\r\nCurrent version: {0}"),
                                    check.CurrentVersion),
                                AppConstants.AppName,
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                            SetStatus(UiLocalization.Text("MainForm.Update.Status.AlreadyLatest", "MyCodex is up to date"));
                        }

                        return;
                    }

                    var releaseNotes = ResolveUpdateReleaseNotes(check);
                    DialogResult updateDecision;
                    using (var promptForm = new AppUpdatePromptForm(
                               check.CurrentVersion,
                               check.LatestVersion,
                               releaseNotes))
                    {
                        updateDecision = promptForm.ShowDialog(this);
                    }

                    if (updateDecision == DialogResult.Ignore)
                    {
                        SetStatus(UiLocalization.Text("MainForm.Update.Status.Ignored", "Update ignored"));
                        return;
                    }

                    if (updateDecision != DialogResult.OK)
                    {
                        SetStatus(UiLocalization.Text("MainForm.Update.Status.Cancelled", "Update cancelled"));
                        return;
                    }

                    SetStatus(UiLocalization.Text("MainForm.Update.Status.Downloading", "Downloading update..."));
                    AppUpdateDownloadResult download;
                    using (var downloadForm = new AppUpdateDownloadForm(service, check))
                    {
                        if (downloadForm.ShowDialog(this) != DialogResult.OK || downloadForm.DownloadResult == null)
                        {
                            SetStatus(UiLocalization.Text("MainForm.Update.Status.Cancelled", "Update cancelled"));
                            return;
                        }

                        download = downloadForm.DownloadResult;
                    }

                    if (await InstallDownloadedUpdateAsync(download.InstallerPath) && !IsDisposed)
                        SetStatus(UiLocalization.Text("MainForm.Update.Status.InstallerStarted", "Update installer started"));
                }
            }
            catch (OperationCanceledException)
            {
                if (!await WaitForTargetExitDecisionAsync()) return;
                if (!automatic)
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("MainForm.Update.Message.Timeout", "Update check timed out."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                SetHighPriorityStatus(UiLocalization.Text("MainForm.Update.Status.Timeout", "Update check timed out"));
            }
            catch (Exception ex)
            {
                if (!await WaitForTargetExitDecisionAsync()) return;
                if (!automatic)
                {
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                AppendLog(UiLocalization.Text("MainForm.Update.Log.FailedPrefix", "Update failed: ") + ex);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Update.Status.Failed", "Update failed"));
            }
            finally
            {
                _isUpdateCheckInProgress = false;
                if (!IsDisposed) _checkUpdatesMenuItem.Enabled = true;
            }
        }

        private static string ResolveUpdateReleaseNotes(AppUpdateCheckResult check)
        {
            if (check == null)
            {
                return null;
            }

            var preferChinese = string.Equals(
                CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                "zh",
                StringComparison.OrdinalIgnoreCase);
            if (preferChinese)
            {
                return FirstNonEmpty(check.ReleaseNotesZhCn, check.ReleaseNotesEnUs, check.ReleaseNotes);
            }

            return FirstNonEmpty(check.ReleaseNotesEnUs, check.ReleaseNotesZhCn, check.ReleaseNotes);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return null;
            }

            for (var i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i];
                }
            }

            return null;
        }

        private void ShowMainWindowTrayMenuItem_Click(object sender, EventArgs e)
        {
            ShowMainWindowFromTray();
        }

        private void HideMainWindowTrayMenuItem_Click(object sender, EventArgs e)
        {
            HideMainWindowToTray();
        }

        private void ExitTrayMenuItem_Click(object sender, EventArgs e)
        {
            RequestApplicationExit();
        }

        private void TrayContextMenu_Popup(object sender, EventArgs e)
        {
            UpdateTargetTrayText();
            var isVisible = Visible && WindowState != FormWindowState.Minimized;
            _showMainWindowTrayMenuItem.Enabled = !isVisible;
            _hideMainWindowTrayMenuItem.Enabled = isVisible;
        }

        private void TrayIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindowFromTray();
            }
        }

        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            ShowMainWindowFromTray();
        }

        private async void RequestApplicationExit()
        {
            await RequestSingleTargetExitAsync(true);
        }

        private bool ConfirmApplicationExit()
        {
            var owner = Visible ? this : null;
            return ExitConfirmationMessageBox.Show(
                owner,
                BuildApplicationExitPrompt(),
                AppConstants.AppName) == DialogResult.Yes;
        }

        private string BuildApplicationExitPrompt()
        {
            var prompt = HasActiveRuntimeWorkForExit()
                ? UiLocalization.Text(
                    "MainForm.Message.ConfirmExit.Active",
                    "Coding or Office work is still active. Exit MyCodex and stop active responses, commands, and automations in both modes?")
                : UiLocalization.Text(
                    "MainForm.Message.ConfirmExit",
                    "Are you sure you want to exit MyCodex?");
            return GetCurrentRuntimeDisplayName() + Environment.NewLine + prompt;
        }

        private bool HasActiveRuntimeWorkForExit()
        {
            if (_busyThreadIds.Count > 0 ||
                _isCommandRunning ||
                _sessionCommandTerminals.Values.Any(host => host != null && host.HasActiveCommand) ||
                _sessionWorkbenches.Values.Any(form => form != null && form.IsBusy) ||
                _subAgentWorkbenches.Values.Any(form => form != null && form.IsBusy) ||
                _gitWorkbenchForms.Values.Any(form => form != null && form.IsBusy) ||
                GetRuntimeWindowGroupForms().Any(form => form is IRuntimeTransitionWindow operation && operation.IsBusyForRuntimeTransition))
            {
                return true;
            }

            try
            {
                var snapshot = _automationStore != null ? _automationStore.Load() : null;
                return snapshot != null &&
                       snapshot.Runs != null &&
                       snapshot.Runs.Any(run => run != null && run.Status == AutomationRunStatus.Running);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to inspect active automations during exit: {0}", ex.Message);
                return false;
            }
        }

        private void HideMainWindowToTray()
        {
            if (TryActivateTargetModalWindow()) return;
            if (!Visible)
            {
                return;
            }

            CaptureFocusBeforeTrayHide();
            Hide();
        }

        private void ShowMainWindowFromTray()
        {
            if (IsDisposed || _isExitRequested || TryActivateTargetModalWindow()) return;
            if (!Visible)
            {
                Show();
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            ShowInTaskbar = true;
            ForceForegroundWindow();
            Activate();
            BringToFront();
            this.BeginInvokeSafe(RestoreFocusAfterTrayShow);
        }

        private void ForceForegroundWindow()
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            NativeMethods.ShowWindow(Handle, NativeMethods.ShowWindowRestore);
            NativeMethods.SetForegroundWindow(Handle);
        }

        private void CaptureFocusBeforeTrayHide()
        {
            var focusedControl = FindFocusedControl(this);
            if (focusedControl != null && !ReferenceEquals(focusedControl, this))
            {
                _lastFocusedControlBeforeTrayHide = focusedControl;
                return;
            }

            if (ActiveControl != null && !ActiveControl.IsDisposed)
            {
                _lastFocusedControlBeforeTrayHide = ActiveControl;
            }
        }

        private void RestoreFocusAfterTrayShow()
        {
            if (TryRestoreFocus(_lastFocusedControlBeforeTrayHide))
            {
                return;
            }

            SetInitialFocus();
        }

        private static Control FindFocusedControl(Control parent)
        {
            if (parent == null || parent.IsDisposed || !parent.ContainsFocus)
            {
                return null;
            }

            foreach (Control child in parent.Controls)
            {
                var focusedChild = FindFocusedControl(child);
                if (focusedChild != null)
                {
                    return focusedChild;
                }
            }

            return parent.Focused ? parent : null;
        }

        private static class NativeMethods
        {
            public const int ShowWindowRestore = 9;

            [DllImport("user32.dll")]
            public static extern bool ShowWindow(IntPtr hWnd, int command);

            [DllImport("user32.dll")]
            public static extern bool SetForegroundWindow(IntPtr hWnd);
        }

        private bool TryRestoreFocus(Control control)
        {
            if (control == null || control.IsDisposed || !control.Visible || !control.Enabled || !control.CanFocus)
            {
                return false;
            }

            ActiveControl = control;
            return control.Focus();
        }
    }
}
